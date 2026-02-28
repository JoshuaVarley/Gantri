using Gantri.Configuration;
using Microsoft.Extensions.Configuration;

namespace Gantri.Integration.Tests;

public sealed class DotEnvConfigurationTests : IDisposable
{
    private readonly string _tempDir;

    public DotEnvConfigurationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-dotenv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void AddDotEnvFile_LoadsValueIntoConfigurationAndProcessEnvironment()
    {
        const string key = "GANTRI_DOTENV_PROCESS_KEY";
        const string value = "from-dotenv";
        var envPath = Path.Combine(_tempDir, ".env");

        Environment.SetEnvironmentVariable(key, null);
        File.WriteAllText(envPath, $"{key}={value}{Environment.NewLine}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddDotEnvFile(envPath, optional: false)
                .Build();

            configuration[key].Should().Be(value);
            Environment.GetEnvironmentVariable(key).Should().Be(value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void AddDotEnvFile_DoesNotOverrideExistingProcessEnvironmentVariable()
    {
        const string key = "GANTRI_DOTENV_PRECEDENCE_KEY";
        const string existingValue = "already-set";
        const string dotenvValue = "from-dotenv";
        var envPath = Path.Combine(_tempDir, ".env");

        Environment.SetEnvironmentVariable(key, existingValue);
        File.WriteAllText(envPath, $"{key}={dotenvValue}{Environment.NewLine}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddDotEnvFile(envPath, optional: false)
                .Build();

            configuration[key].Should().Be(dotenvValue);
            Environment.GetEnvironmentVariable(key).Should().Be(existingValue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void AddDotEnvFile_ParsesExportPrefix()
    {
        const string key = "GANTRI_DOTENV_EXPORT_KEY";
        const string value = "exported-value";
        var envPath = Path.Combine(_tempDir, ".env");

        Environment.SetEnvironmentVariable(key, null);
        File.WriteAllText(envPath, $"export {key}={value}{Environment.NewLine}");

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddDotEnvFile(envPath, optional: false)
                .Build();

            configuration[key].Should().Be(value);
            Environment.GetEnvironmentVariable(key).Should().Be(value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }
}
