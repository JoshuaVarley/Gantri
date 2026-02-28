using Gantri.Plugins.Wasm;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gantri.Plugins.Wasm.Tests;

public class WasmPluginHostTests
{
    [Fact]
    public void DefaultFuelBudget_IsOneMillion()
    {
        var host = new WasmPluginHost(NullLogger<WasmPluginHost>.Instance);
        host.DefaultFuelBudget.Should().Be(1_000_000L);
        host.Dispose();
    }

    [Fact]
    public void DefaultMemoryLimit_Is16MB()
    {
        var host = new WasmPluginHost(NullLogger<WasmPluginHost>.Instance);
        host.DefaultMemoryLimit.Should().Be(16 * 1024 * 1024L);
        host.Dispose();
    }

    [Fact]
    public void Constructor_CreatesEngineWithFuelConsumption()
    {
        // WasmPluginHost creates an Engine with fuel consumption enabled.
        // We verify by constructing it — if fuel consumption config fails, this throws.
        var host = new WasmPluginHost(NullLogger<WasmPluginHost>.Instance);
        host.Should().NotBeNull();
        host.Engine.Should().NotBeNull();
        host.Dispose();
    }

    [Fact]
    public void LoadModule_NonexistentFile_Throws()
    {
        var host = new WasmPluginHost(NullLogger<WasmPluginHost>.Instance);

        var act = () => host.LoadModule("/nonexistent/path.wasm");
        act.Should().Throw<FileNotFoundException>();
        host.Dispose();
    }
}
