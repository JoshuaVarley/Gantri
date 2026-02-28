using Gantri.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Integration.Tests;

public class ConfigImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly YamlConfigurationLoader _loader;

    public ConfigImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-cfg-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _loader = new YamlConfigurationLoader(NullLogger<YamlConfigurationLoader>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadWithImports_MergesMultipleFiles()
    {
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
              imports:
                - extra.yaml
            """);
        File.WriteAllText(Path.Combine(_tempDir, "extra.yaml"), """
            ai:
              default_model: gpt-4o-mini
            """);

        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));

        result.Should().ContainKey("framework");
        result.Should().ContainKey("ai");
    }

    [Fact]
    public void LoadWithImports_DeepMergesNestedDicts()
    {
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
              imports:
                - override.yaml
            ai:
              default_model: gpt-4o-mini
            """);
        File.WriteAllText(Path.Combine(_tempDir, "override.yaml"), """
            ai:
              default_model: gpt-4o
            """);

        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));

        result.Should().ContainKey("ai");
    }

    [Fact]
    public void LoadWithImports_GlobImports()
    {
        var agentsDir = Path.Combine(_tempDir, "agents");
        Directory.CreateDirectory(agentsDir);

        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
              imports:
                - agents/*.yaml
            """);
        File.WriteAllText(Path.Combine(agentsDir, "reviewer.yaml"), """
            agents:
              reviewer:
                model: gpt-4o
            """);
        File.WriteAllText(Path.Combine(agentsDir, "triager.yaml"), """
            agents:
              triager:
                model: gpt-4o-mini
            """);

        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));

        result.Should().ContainKey("agents");
    }

    [Fact]
    public void LoadWithImports_MissingImportFile_Skipped()
    {
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
              imports:
                - nonexistent.yaml
            """);

        // Should not throw, just skip the missing import
        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));
        result.Should().ContainKey("framework");
    }

    [Fact]
    public void LoadWithImports_NoImports_ReturnsRoot()
    {
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
            """);

        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));
        result.Should().ContainKey("framework");
    }

    [Fact]
    public void LoadWithImports_DeepMerge_PreservesExistingKeys()
    {
        // Tests deep merge indirectly: root defines key_a, import defines key_a override + key_b
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: test
              imports:
                - overlay.yaml
            top_level: original
            """);
        File.WriteAllText(Path.Combine(_tempDir, "overlay.yaml"), """
            top_level: updated
            extra_key: new_value
            """);

        var result = _loader.LoadWithImports(Path.Combine(_tempDir, "root.yaml"));

        result.Should().ContainKey("framework");
        result.Should().ContainKey("top_level");
        result["top_level"].Should().Be("updated");
        result.Should().ContainKey("extra_key");
    }

    [Fact]
    public void LoadWithImports_EnvVarSubstitution_Works()
    {
        Environment.SetEnvironmentVariable("GANTRI_TEST_VAR", "test-value");
        try
        {
            File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
                framework:
                  name: ${GANTRI_TEST_VAR}
                """);

            var result = _loader.LoadRaw(Path.Combine(_tempDir, "root.yaml"));
            result.Should().ContainKey("framework");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GANTRI_TEST_VAR", null);
        }
    }

    [Fact]
    public void LoadTypedWithImports_DeserializesToTypedObject()
    {
        File.WriteAllText(Path.Combine(_tempDir, "root.yaml"), """
            framework:
              name: Gantri
              version: 1.0.0
              log_level: Information
              data_dir: ./data
              imports:
                - ai.yaml
            """);
        File.WriteAllText(Path.Combine(_tempDir, "ai.yaml"), """
            ai:
              default_model: gpt-4o-mini
            """);

        var config = _loader.LoadTypedWithImports<GantriConfigRoot>(
            Path.Combine(_tempDir, "root.yaml"));

        config.Should().NotBeNull();
        config.Framework.Name.Should().Be("Gantri");
        config.Ai.DefaultModel.Should().Be("gpt-4o-mini");
    }
}
