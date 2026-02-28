using Gantri.Abstractions.Plugins;
using Gantri.Plugins.Wasm;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Plugins.Wasm.Tests;

public class PluginCapabilityManagerTests
{
    private readonly PluginCapabilityManager _manager = new(NullLogger<PluginCapabilityManager>.Instance);

    private static PluginManifest CreateManifest(
        List<string>? required = null,
        List<string>? optional = null)
    {
        return new PluginManifest
        {
            Name = "test-plugin",
            Version = "1.0.0",
            Capabilities = new PluginCapabilities
            {
                Required = required ?? [],
                Optional = optional ?? []
            }
        };
    }

    [Fact]
    public void ResolveCapabilities_NoCapabilities_GrantsLogOnly()
    {
        var manifest = CreateManifest();
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.Log);
        caps.Should().NotHaveFlag(PluginCapability.ConfigRead);
        caps.Should().NotHaveFlag(PluginCapability.AiComplete);
        caps.Should().NotHaveFlag(PluginCapability.FsRead);
        caps.Should().NotHaveFlag(PluginCapability.FsWrite);
        caps.Should().NotHaveFlag(PluginCapability.HttpRequest);
        caps.Should().NotHaveFlag(PluginCapability.McpCall);
    }

    [Fact]
    public void ResolveCapabilities_RequiredCapabilities_Granted()
    {
        var manifest = CreateManifest(required: ["config_read", "ai_complete"]);
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.Log);
        caps.Should().HaveFlag(PluginCapability.ConfigRead);
        caps.Should().HaveFlag(PluginCapability.AiComplete);
    }

    [Fact]
    public void ResolveCapabilities_OptionalCapabilities_Granted()
    {
        var manifest = CreateManifest(optional: ["fs_read", "http_request"]);
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.FsRead);
        caps.Should().HaveFlag(PluginCapability.HttpRequest);
    }

    [Fact]
    public void ResolveCapabilities_AllCapabilities_GrantsAll()
    {
        var manifest = CreateManifest(
            required: ["config_read", "ai_complete", "fs_read", "fs_write", "http_request", "mcp_call"]);
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.Log);
        caps.Should().HaveFlag(PluginCapability.ConfigRead);
        caps.Should().HaveFlag(PluginCapability.AiComplete);
        caps.Should().HaveFlag(PluginCapability.FsRead);
        caps.Should().HaveFlag(PluginCapability.FsWrite);
        caps.Should().HaveFlag(PluginCapability.HttpRequest);
        caps.Should().HaveFlag(PluginCapability.McpCall);
    }

    [Fact]
    public void ResolveCapabilities_UnknownRequiredCapability_Throws()
    {
        var manifest = CreateManifest(required: ["teleport"]);

        var act = () => _manager.ResolveCapabilities(manifest);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown capability*teleport*");
    }

    [Fact]
    public void ResolveCapabilities_UnknownOptionalCapability_Skips()
    {
        var manifest = CreateManifest(optional: ["teleport", "fs_read"]);
        var caps = _manager.ResolveCapabilities(manifest);

        // Unknown optional is skipped, but known optional is still granted
        caps.Should().HaveFlag(PluginCapability.FsRead);
        caps.Should().HaveFlag(PluginCapability.Log);
    }

    [Fact]
    public void ResolveCapabilities_CaseInsensitive()
    {
        var manifest = CreateManifest(required: ["Config_Read", "AI_COMPLETE"]);
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.ConfigRead);
        caps.Should().HaveFlag(PluginCapability.AiComplete);
    }

    [Fact]
    public void ResolveCapabilities_MixedRequiredAndOptional()
    {
        var manifest = CreateManifest(
            required: ["config_read"],
            optional: ["mcp_call"]);
        var caps = _manager.ResolveCapabilities(manifest);

        caps.Should().HaveFlag(PluginCapability.Log);
        caps.Should().HaveFlag(PluginCapability.ConfigRead);
        caps.Should().HaveFlag(PluginCapability.McpCall);
        caps.Should().NotHaveFlag(PluginCapability.FsRead);
    }
}
