using System.Text;
using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Wasmtime;

namespace Gantri.Plugins.Wasm;

/// <summary>
/// A WASM plugin loaded via Wasmtime. Communicates with the WASM module using
/// shared memory with JSON serialization.
///
/// The WASM module must export:
///   - memory: linear memory
///   - allocate(size: i32) -> i32: allocates a buffer, returns pointer
///   - execute(action_ptr: i32, action_len: i32, input_ptr: i32, input_len: i32) -> i32: runs an action
///   - get_output_ptr() -> i32: returns pointer to the last output
///   - get_output_len() -> i32: returns length of the last output
/// </summary>
internal sealed class WasmPlugin : IPlugin
{
    private readonly Store _store;
    private readonly Memory _memory;
    private readonly Func<int, int>? _allocate;
    private readonly Func<int, int, int, int, int>? _execute;
    private readonly Func<int>? _getOutputPtr;
    private readonly Func<int>? _getOutputLen;

    public string Name => Manifest.Name;
    public string Version => Manifest.Version;
    public PluginType Type => PluginType.Wasm;
    public PluginManifest Manifest { get; }
    public IReadOnlyList<string> ActionNames { get; }

    public WasmPlugin(PluginManifest manifest, Store store, Instance instance)
    {
        Manifest = manifest;
        _store = store;
        ActionNames = manifest.Exports.Actions.Select(a => a.Name).ToList();

        _memory = instance.GetMemory("memory")
            ?? throw new InvalidOperationException($"WASM plugin '{manifest.Name}' does not export 'memory'.");

        _allocate = instance.GetFunction<int, int>("allocate");
        _execute = instance.GetFunction<int, int, int, int, int>("execute");
        _getOutputPtr = instance.GetFunction<int>("get_output_ptr");
        _getOutputLen = instance.GetFunction<int>("get_output_len");
    }

    public Task<PluginActionResult> ExecuteActionAsync(string actionName, PluginActionInput input, CancellationToken cancellationToken = default)
    {
        if (_execute is null || _allocate is null || _getOutputPtr is null || _getOutputLen is null)
            return Task.FromResult(PluginActionResult.Fail(
                $"WASM plugin '{Name}' is missing required exports (allocate, execute, get_output_ptr, get_output_len)."));

        try
        {
            var actionBytes = Encoding.UTF8.GetBytes(actionName);
            var inputJson = JsonSerializer.Serialize(input.Parameters);
            var inputBytes = Encoding.UTF8.GetBytes(inputJson);

            // Write action name into WASM memory
            var actionPtr = _allocate(actionBytes.Length);
            actionBytes.CopyTo(_memory.GetSpan(actionPtr, actionBytes.Length));

            // Write input JSON into WASM memory
            var inputPtr = _allocate(inputBytes.Length);
            inputBytes.CopyTo(_memory.GetSpan(inputPtr, inputBytes.Length));

            // Call execute
            var status = _execute(actionPtr, actionBytes.Length, inputPtr, inputBytes.Length);

            // Read output
            var outputPtr = _getOutputPtr();
            var outputLen = _getOutputLen();
            var outputJson = _memory.ReadString(outputPtr, outputLen, Encoding.UTF8);

            return status == 0
                ? Task.FromResult(PluginActionResult.Ok(outputJson))
                : Task.FromResult(PluginActionResult.Fail(outputJson ?? "WASM execution failed"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PluginActionResult.Fail($"WASM execution error: {ex.Message}"));
        }
    }

    public ValueTask DisposeAsync()
    {
        _store.Dispose();
        return ValueTask.CompletedTask;
    }
}
