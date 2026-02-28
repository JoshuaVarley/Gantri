using Wasmtime;
using Microsoft.Extensions.Logging;

namespace Gantri.Plugins.Wasm;

/// <summary>
/// Manages the Wasmtime engine lifecycle. Shared across all WASM plugin instances.
/// Fuel consumption is enabled for resource metering.
/// </summary>
public sealed class WasmPluginHost : IDisposable
{
    private readonly ILogger<WasmPluginHost> _logger;

    public Engine Engine { get; }

    /// <summary>
    /// Default fuel budget per plugin execution (1 million units).
    /// </summary>
    public long DefaultFuelBudget { get; set; } = 1_000_000;

    /// <summary>
    /// Default memory limit per plugin instance (16 MB).
    /// </summary>
    public long DefaultMemoryLimit { get; set; } = 16 * 1024 * 1024;

    public WasmPluginHost(ILogger<WasmPluginHost> logger)
    {
        _logger = logger;
        var config = new Config();
        config.WithFuelConsumption(true);
        Engine = new Engine(config);
        _logger.LogInformation("Wasmtime engine initialized with fuel metering enabled");
    }

    public Module LoadModule(string wasmPath)
    {
        if (!File.Exists(wasmPath))
            throw new FileNotFoundException($"WASM module not found: {wasmPath}");

        _logger.LogDebug("Loading WASM module from {Path}", wasmPath);
        return Module.FromFile(Engine, wasmPath);
    }

    public void Dispose()
    {
        Engine.Dispose();
        _logger.LogInformation("Wasmtime engine disposed");
    }
}
