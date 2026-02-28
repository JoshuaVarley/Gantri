using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Plugins.Native.Tests;

public class NativePluginLoaderTests
{
    private static string GetHelloWorldPluginPath()
    {
        // Navigate from test bin to the hello-world plugin's build output
        var testDir = AppContext.BaseDirectory;
        var solutionRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        return Path.Combine(solutionRoot, "plugins", "built-in", "hello-world", "bin", "Debug", "net10.0");
    }

    [Fact]
    public void LoadManifest_ValidManifest_Deserializes()
    {
        var pluginPath = GetHelloWorldPluginPath();
        var manifest = NativePluginLoader.LoadManifest(pluginPath);

        manifest.Name.Should().Be("hello-world");
        manifest.Type.Should().Be(PluginType.Native);
        manifest.Entry.Should().Be("HelloWorld.Plugin.dll");
        manifest.Exports.Actions.Should().HaveCount(1);
        manifest.Exports.Actions[0].Name.Should().Be("hello");
    }

    [Fact]
    public async Task LoadAsync_ValidPlugin_LoadsSuccessfully()
    {
        var pluginPath = GetHelloWorldPluginPath();
        var manifest = NativePluginLoader.LoadManifest(pluginPath);
        var loader = new NativePluginLoader(new NativePluginValidator(), NullLogger<NativePluginLoader>.Instance);

        var plugin = await loader.LoadAsync(pluginPath, manifest);

        plugin.Name.Should().Be("hello-world");
        plugin.Type.Should().Be(PluginType.Native);
        plugin.ActionNames.Should().Contain("hello");
    }

    [Fact]
    public async Task ExecuteAction_HelloAction_ReturnsGreeting()
    {
        var pluginPath = GetHelloWorldPluginPath();
        var manifest = NativePluginLoader.LoadManifest(pluginPath);
        var loader = new NativePluginLoader(new NativePluginValidator(), NullLogger<NativePluginLoader>.Instance);

        var plugin = await loader.LoadAsync(pluginPath, manifest);
        var result = await plugin.ExecuteActionAsync("hello", new PluginActionInput
        {
            ActionName = "hello",
            Parameters = new Dictionary<string, object?> { ["name"] = "Gantri" }
        });

        result.Success.Should().BeTrue();
        result.Output.Should().Be("Hello, Gantri! From the hello-world plugin.");
    }

    [Fact]
    public async Task ExecuteAction_UnknownAction_ReturnsFailure()
    {
        var pluginPath = GetHelloWorldPluginPath();
        var manifest = NativePluginLoader.LoadManifest(pluginPath);
        var loader = new NativePluginLoader(new NativePluginValidator(), NullLogger<NativePluginLoader>.Instance);

        var plugin = await loader.LoadAsync(pluginPath, manifest);
        var result = await plugin.ExecuteActionAsync("nonexistent", new PluginActionInput
        {
            ActionName = "nonexistent"
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("nonexistent");
    }

    [Fact]
    public async Task UnloadAsync_LoadedPlugin_Unloads()
    {
        var pluginPath = GetHelloWorldPluginPath();
        var manifest = NativePluginLoader.LoadManifest(pluginPath);
        var loader = new NativePluginLoader(new NativePluginValidator(), NullLogger<NativePluginLoader>.Instance);

        await loader.LoadAsync(pluginPath, manifest);
        await loader.UnloadAsync("hello-world");

        // Unloading again should be a no-op
        await loader.UnloadAsync("hello-world");
    }

    [Fact]
    public void LoadManifest_MissingManifest_Throws()
    {
        var act = () => NativePluginLoader.LoadManifest("/nonexistent/path");
        act.Should().Throw<FileNotFoundException>();
    }
}
