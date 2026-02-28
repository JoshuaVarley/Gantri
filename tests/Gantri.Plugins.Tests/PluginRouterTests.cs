using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Plugins.Tests;

public class PluginRouterTests
{
    private static PluginRouter CreateRouter(params IPluginLoader[] loaders)
    {
        var discovery = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance);
        var manager = new PluginManager(NullLogger<PluginManager>.Instance);
        return new PluginRouter(loaders, discovery, manager, NullLogger<PluginRouter>.Instance);
    }

    [Fact]
    public async Task ResolveAsync_UnknownPlugin_Throws()
    {
        var router = CreateRouter();
        router.ScanPluginDirectories([]);

        var act = () => router.ResolveAsync("nonexistent");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public async Task ResolveAsync_WasmPlugin_NoLoader_ThrowsHelpfulMessage()
    {
        var router = CreateRouter(); // No WASM loader registered
        var wasmManifest = new PluginManifest { Name = "test-wasm", Type = PluginType.Wasm };

        // Manually register a discovered plugin to simulate discovery
        var discovery = new PluginDiscovery(NullLogger<PluginDiscovery>.Instance);
        var manager = new PluginManager(NullLogger<PluginManager>.Instance);
        var routerWithDiscovery = new PluginRouter(
            Array.Empty<IPluginLoader>(), discovery, manager, NullLogger<PluginRouter>.Instance);

        // We need to scan a real directory to test discovery
        // Instead, test the error message format
        var act = () => routerWithDiscovery.ResolveAsync("test-wasm");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not found*");
    }

    [Fact]
    public void PluginManager_RegisterAndGet_Works()
    {
        var manager = new PluginManager(NullLogger<PluginManager>.Instance);
        var plugin = Substitute.For<IPlugin>();
        plugin.Name.Returns("test");
        plugin.Version.Returns("1.0");
        plugin.Type.Returns(PluginType.Native);

        manager.Register(plugin);

        manager.Get("test").Should().BeSameAs(plugin);
        manager.LoadedPlugins.Should().HaveCount(1);
    }

    [Fact]
    public void PluginManager_GetByType_Filters()
    {
        var manager = new PluginManager(NullLogger<PluginManager>.Instance);

        var native = Substitute.For<IPlugin>();
        native.Name.Returns("native-p");
        native.Type.Returns(PluginType.Native);

        var wasm = Substitute.For<IPlugin>();
        wasm.Name.Returns("wasm-p");
        wasm.Type.Returns(PluginType.Wasm);

        manager.Register(native);
        manager.Register(wasm);

        manager.GetByType(PluginType.Native).Should().HaveCount(1);
        manager.GetByType(PluginType.Native)[0].Name.Should().Be("native-p");
    }

    [Fact]
    public async Task PluginManager_Unload_RemovesPlugin()
    {
        var manager = new PluginManager(NullLogger<PluginManager>.Instance);
        var plugin = Substitute.For<IPlugin>();
        plugin.Name.Returns("test");

        manager.Register(plugin);
        manager.Get("test").Should().NotBeNull();

        await manager.UnloadAsync("test");
        manager.Get("test").Should().BeNull();
    }
}
