using Gantri.Abstractions.Configuration;
using Gantri.Cli.Commands;
using Gantri.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Integration.Tests;

public class ConfigValidateCommandTests
{
    [Fact]
    public async Task ExecuteAsync_UsesImportedConfigFiles_WhenValidating()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"gantri-config-validate-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(tempDir);

        try
        {
            var rootPath = Path.Combine(tempDir, "gantri.yaml");
            var aiPath = Path.Combine(tempDir, "ai.yaml");

            await File.WriteAllTextAsync(
                rootPath,
                """
framework:
  imports:
    - ai.yaml
ai:
  default_model: gpt4
agents:
  writer:
    provider: openai
    model: gpt4
"""
            );

            await File.WriteAllTextAsync(
                aiPath,
                """
ai:
  providers:
    openai:
      endpoint: https://example.openai.azure.com/
      models:
        gpt4:
          id: gpt-4o
plugins:
  dirs: []
"""
            );

            var loader = new YamlConfigurationLoader(NullLogger<YamlConfigurationLoader>.Instance);
            var command = new ConfigValidateCommand(loader);

            var result = await command.ExecuteAsync(
                null!,
                new ConfigValidateCommand.Settings { ConfigPath = rootPath }
            );

            result.Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Validate_ResponsesApiType_IsAccepted()
    {
        var config = new GantriConfigRoot
        {
            Ai = new AiOptions
            {
                Providers = new Dictionary<string, AiProviderOptions>
                {
                    ["openai"] = new AiProviderOptions
                    {
                        Endpoint = "https://example.openai.azure.com/",
                        ApiKey = "key",
                        Models = new Dictionary<string, AiModelOptions>
                        {
                            ["codex"] = new AiModelOptions
                            {
                                Id = "gpt-5.1-codex-mini",
                                ApiType = "responses",
                            },
                        },
                    },
                },
            },
        };

        var validator = new ConfigValidator(
            NullLogger<ConfigValidator>.Instance
        );

        var errors = validator.Validate(config);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ChatApiType_IsAccepted()
    {
        var config = new GantriConfigRoot
        {
            Ai = new AiOptions
            {
                Providers = new Dictionary<string, AiProviderOptions>
                {
                    ["openai"] = new AiProviderOptions
                    {
                        Endpoint = "https://example.openai.azure.com/",
                        ApiKey = "key",
                        Models = new Dictionary<string, AiModelOptions>
                        {
                            ["gpt4"] = new AiModelOptions
                            {
                                Id = "gpt-4o",
                                ApiType = "chat",
                            },
                        },
                    },
                },
            },
        };

        var validator = new ConfigValidator(
            NullLogger<ConfigValidator>.Instance
        );

        var errors = validator.Validate(config);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_InvalidApiType_ReturnsError()
    {
        var config = new GantriConfigRoot
        {
            Ai = new AiOptions
            {
                Providers = new Dictionary<string, AiProviderOptions>
                {
                    ["openai"] = new AiProviderOptions
                    {
                        Endpoint = "https://example.openai.azure.com/",
                        ApiKey = "key",
                        Models = new Dictionary<string, AiModelOptions>
                        {
                            ["bad"] = new AiModelOptions
                            {
                                Id = "gpt-4o",
                                ApiType = "completions",
                            },
                        },
                    },
                },
            },
        };

        var validator = new ConfigValidator(
            NullLogger<ConfigValidator>.Instance
        );

        var errors = validator.Validate(config);

        errors.Should().ContainSingle()
            .Which.Should().Contain("invalid api_type 'completions'");
    }
}
