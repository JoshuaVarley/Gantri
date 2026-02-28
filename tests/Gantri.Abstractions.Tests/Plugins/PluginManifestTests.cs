using System.Text.Json;
using Gantri.Abstractions.Plugins;

namespace Gantri.Abstractions.Tests.Plugins;

public class PluginManifestTests
{
    private const string NativeManifestJson = """
    {
        "name": "file-globber",
        "version": "1.0.0",
        "type": "native",
        "description": "High-performance file pattern matching",
        "entry": "Gantri.Plugins.FileGlobber.dll",
        "trust": "first-party",
        "capabilities": {
            "required": ["fs-read"],
            "optional": ["fs-write"]
        },
        "exports": {
            "actions": [
                { "name": "glob-files", "description": "Find files matching glob patterns" }
            ],
            "hooks": []
        }
    }
    """;

    private const string WasmManifestJson = """
    {
        "name": "devops-ticket-poller",
        "version": "1.0.0",
        "type": "wasm",
        "description": "Polls Azure DevOps for new tickets",
        "entry": "plugin.wasm",
        "capabilities": {
            "required": ["http", "config-read", "ai-complete"],
            "optional": ["fs-read"]
        },
        "exports": {
            "actions": [
                { "name": "poll-tickets", "description": "Query DevOps API for new work items" }
            ],
            "hooks": [
                { "event": "scheduler:tickerq:job-start:before", "function": "on_job_start" }
            ]
        }
    }
    """;

    [Fact]
    public void Deserialize_NativeManifest_RoundTrips()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(NativeManifestJson)!;

        manifest.Name.Should().Be("file-globber");
        manifest.Version.Should().Be("1.0.0");
        manifest.Type.Should().Be(PluginType.Native);
        manifest.Entry.Should().Be("Gantri.Plugins.FileGlobber.dll");
        manifest.Trust.Should().Be("first-party");
        manifest.Capabilities.Required.Should().Contain("fs-read");
        manifest.Capabilities.Optional.Should().Contain("fs-write");
        manifest.Exports.Actions.Should().HaveCount(1);
        manifest.Exports.Actions[0].Name.Should().Be("glob-files");

        var json = JsonSerializer.Serialize(manifest);
        var roundTripped = JsonSerializer.Deserialize<PluginManifest>(json)!;
        roundTripped.Name.Should().Be(manifest.Name);
        roundTripped.Type.Should().Be(manifest.Type);
    }

    [Fact]
    public void Deserialize_WasmManifest_IncludesHooks()
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(WasmManifestJson)!;

        manifest.Type.Should().Be(PluginType.Wasm);
        manifest.Exports.Hooks.Should().HaveCount(1);
        manifest.Exports.Hooks[0].Event.Should().Be("scheduler:tickerq:job-start:before");
        manifest.Exports.Hooks[0].Function.Should().Be("on_job_start");
    }

    [Fact]
    public void PluginType_Enum_HasExpectedValues()
    {
        Enum.GetValues<PluginType>().Should().HaveCount(2);
        Enum.GetValues<PluginType>().Should().Contain(PluginType.Native);
        Enum.GetValues<PluginType>().Should().Contain(PluginType.Wasm);
    }

    [Fact]
    public void PluginCapability_Flags_ArePowersOfTwo()
    {
        var values = Enum.GetValues<PluginCapability>()
            .Where(v => v != PluginCapability.None)
            .ToList();

        foreach (var value in values)
        {
            var intVal = (int)value;
            (intVal & (intVal - 1)).Should().Be(0, $"{value} should be a power of 2");
        }
    }

    [Fact]
    public void PluginCapability_CanCombineFlags()
    {
        var combined = PluginCapability.FsRead | PluginCapability.FsWrite | PluginCapability.Log;
        combined.HasFlag(PluginCapability.FsRead).Should().BeTrue();
        combined.HasFlag(PluginCapability.FsWrite).Should().BeTrue();
        combined.HasFlag(PluginCapability.Log).Should().BeTrue();
        combined.HasFlag(PluginCapability.HttpRequest).Should().BeFalse();
    }
}
