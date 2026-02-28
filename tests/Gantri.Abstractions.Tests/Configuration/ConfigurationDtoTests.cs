using System.Text.Json;
using Gantri.Abstractions.Configuration;

namespace Gantri.Abstractions.Tests.Configuration;

public class ConfigurationDtoTests
{
    [Fact]
    public void GantriOptions_Defaults_AreSet()
    {
        var opts = new GantriOptions();
        opts.Name.Should().Be("Gantri");
        opts.LogLevel.Should().Be("Information");
        opts.DataDir.Should().Be("./data");
    }

    [Fact]
    public void AgentDefinition_RoundTrips()
    {
        var agent = new AgentDefinition
        {
            Name = "code-reviewer",
            Model = "sonnet",
            Provider = "anthropic",
            Temperature = 0.2f,
            MaxTokens = 8192,
            Skills = ["code-review", "git-diff-analysis"],
            McpServers = ["github"],
            AllowedActions = ["read-file"],
            Plugins = ["code-quality-checker"]
        };

        var json = JsonSerializer.Serialize(agent);
        var roundTripped = JsonSerializer.Deserialize<AgentDefinition>(json)!;

        roundTripped.Name.Should().Be("code-reviewer");
        roundTripped.Model.Should().Be("sonnet");
        roundTripped.Provider.Should().Be("anthropic");
        roundTripped.Temperature.Should().Be(0.2f);
        roundTripped.MaxTokens.Should().Be(8192);
        roundTripped.Skills.Should().BeEquivalentTo(["code-review", "git-diff-analysis"]);
    }

    [Fact]
    public void AiOptions_ProviderConfig_RoundTrips()
    {
        var opts = new AiOptions
        {
            DefaultModel = "sonnet",
            Providers = new()
            {
                ["anthropic"] = new AiProviderOptions
                {
                    ApiKey = "test-key",
                    Models = new()
                    {
                        ["sonnet"] = new AiModelOptions
                        {
                            Id = "claude-sonnet-4-5-20250929",
                            MaxTokens = 8192,
                            DefaultTemperature = 0.3f
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(opts);
        var roundTripped = JsonSerializer.Deserialize<AiOptions>(json)!;

        roundTripped.DefaultModel.Should().Be("sonnet");
        roundTripped.Providers.Should().ContainKey("anthropic");
        roundTripped.Providers["anthropic"].Models["sonnet"].Id.Should().Be("claude-sonnet-4-5-20250929");
    }

    [Fact]
    public void TelemetryOptions_Defaults_AreReasonable()
    {
        var opts = new TelemetryOptions();
        opts.Enabled.Should().BeTrue();
        opts.ServiceName.Should().Be("gantri");
        opts.Traces.Sampling.Strategy.Should().Be("always_on");
        opts.Traces.Sampling.Ratio.Should().Be(1.0);
    }

    [Fact]
    public void PluginOptions_Defaults_AreEmpty()
    {
        var opts = new PluginOptions();
        opts.Dirs.Should().BeEmpty();
        opts.NativeTrustDirs.Should().BeEmpty();
        opts.Global.Should().BeEmpty();
    }

    [Fact]
    public void HookBinding_RoundTrips()
    {
        var binding = new HookBinding
        {
            Event = "agent:*:tool-use:before",
            Plugin = "policy-enforcer",
            Hook = "validate_tool_use",
            Priority = 50
        };

        var json = JsonSerializer.Serialize(binding);
        var roundTripped = JsonSerializer.Deserialize<HookBinding>(json)!;

        roundTripped.Event.Should().Be("agent:*:tool-use:before");
        roundTripped.Priority.Should().Be(50);
    }
}
