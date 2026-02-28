using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Plugins.Tests;

public class PluginDiscoveryTests
{
    [Fact]
    public void ScanDirectories_NonexistentDir_ReturnsEmpty()
    {
        var discovery = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance);
        var result = discovery.ScanDirectories(["/nonexistent/path"]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanDirectories_HelloWorldPlugin_Discovered()
    {
        var discovery = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance);
        var testDir = AppContext.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        var pluginParent = Path.Combine(solutionRoot, "plugins", "built-in");

        var result = discovery.ScanDirectories([pluginParent]);

        result.Should().ContainSingle(p => p.Manifest.Name == "hello-world");
    }
}
