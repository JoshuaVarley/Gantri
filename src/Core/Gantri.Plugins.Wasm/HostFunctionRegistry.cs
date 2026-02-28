using System.Text;
using System.Text.Json;
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging;
using Wasmtime;

namespace Gantri.Plugins.Wasm;

/// <summary>
/// Registers host functions on a Wasmtime Linker, gated by PluginCapability flags.
/// All host functions use the shared-memory JSON protocol (ptr/len pairs via allocate export).
/// </summary>
public sealed class HostFunctionRegistry
{
    private readonly ILogger<HostFunctionRegistry> _logger;
    private readonly IHostAiService? _aiService;
    private readonly IHostConfigService? _configService;
    private readonly IHostMcpService? _mcpService;
    private readonly IHttpClientFactory? _httpClientFactory;

    public HostFunctionRegistry(
        ILogger<HostFunctionRegistry> logger,
        IHostAiService? aiService = null,
        IHostConfigService? configService = null,
        IHostMcpService? mcpService = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _logger = logger;
        _aiService = aiService;
        _configService = configService;
        _mcpService = mcpService;
        _httpClientFactory = httpClientFactory;
    }

    public void RegisterHostFunctions(
        Linker linker,
        PluginCapability grantedCapabilities,
        string pluginName)
    {
        // env.log — always granted
        RegisterLog(linker, pluginName);

        // env.config_get — requires ConfigRead
        if (grantedCapabilities.HasFlag(PluginCapability.ConfigRead))
            RegisterConfigGet(linker, pluginName);

        // env.ai_complete — requires AiComplete
        if (grantedCapabilities.HasFlag(PluginCapability.AiComplete))
            RegisterAiComplete(linker, pluginName);

        // env.fs_read — requires FsRead
        if (grantedCapabilities.HasFlag(PluginCapability.FsRead))
            RegisterFsRead(linker, pluginName);

        // env.fs_write — requires FsWrite
        if (grantedCapabilities.HasFlag(PluginCapability.FsWrite))
            RegisterFsWrite(linker, pluginName);

        // env.http_request — requires HttpRequest
        if (grantedCapabilities.HasFlag(PluginCapability.HttpRequest))
            RegisterHttpRequest(linker, pluginName);

        // env.mcp_call — requires McpCall
        if (grantedCapabilities.HasFlag(PluginCapability.McpCall))
            RegisterMcpCall(linker, pluginName);

        _logger.LogDebug("Registered host functions for plugin '{Plugin}' with capabilities {Capabilities}",
            pluginName, grantedCapabilities);
    }

    private void RegisterLog(Linker linker, string pluginName)
    {
        linker.DefineFunction("env", "log", (Caller caller, int ptr, int len) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is not null)
            {
                var message = memory.ReadString(ptr, len, Encoding.UTF8);
                _logger.LogInformation("[WASM:{Plugin}] {Message}", pluginName, message);
            }
        });
    }

    private void RegisterConfigGet(Linker linker, string pluginName)
    {
        linker.DefineFunction("env", "config_get", (Caller caller, int keyPtr, int keyLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null || _configService is null) return 0;

            var dotPath = memory.ReadString(keyPtr, keyLen, Encoding.UTF8);
            var value = _configService.GetValue(dotPath);

            if (value is null) return 0;

            return WriteStringToWasm(caller, value);
        });
    }

    private void RegisterAiComplete(Linker linker, string pluginName)
    {
        linker.DefineFunction("env", "ai_complete", (Caller caller, int promptPtr, int promptLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null || _aiService is null) return 0;

            var prompt = memory.ReadString(promptPtr, promptLen, Encoding.UTF8);
            // Blocking call since Wasmtime host functions are synchronous
            var result = _aiService.CompleteAsync(prompt).GetAwaiter().GetResult();

            return WriteStringToWasm(caller, result);
        });
    }

    private void RegisterFsRead(Linker linker, string pluginName)
    {
        var sandbox = Path.GetFullPath(Path.Combine("data", "plugins", pluginName));

        linker.DefineFunction("env", "fs_read", (Caller caller, int pathPtr, int pathLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null) return 0;

            var relativePath = memory.ReadString(pathPtr, pathLen, Encoding.UTF8);
            var fullPath = Path.GetFullPath(Path.Combine(sandbox, relativePath));

            // Sandbox check: path must stay within plugin data directory
            if (!fullPath.StartsWith(Path.GetFullPath(sandbox), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Plugin '{Plugin}' attempted to read outside sandbox: {Path}", pluginName, relativePath);
                return 0;
            }

            if (!File.Exists(fullPath)) return 0;

            var content = File.ReadAllText(fullPath);
            return WriteStringToWasm(caller, content);
        });
    }

    private void RegisterFsWrite(Linker linker, string pluginName)
    {
        var sandbox = Path.GetFullPath(Path.Combine("data", "plugins", pluginName));

        linker.DefineFunction("env", "fs_write", (Caller caller, int pathPtr, int pathLen, int dataPtr, int dataLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null) return -1;

            var relativePath = memory.ReadString(pathPtr, pathLen, Encoding.UTF8);
            var fullPath = Path.GetFullPath(Path.Combine(sandbox, relativePath));

            if (!fullPath.StartsWith(Path.GetFullPath(sandbox), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Plugin '{Plugin}' attempted to write outside sandbox: {Path}", pluginName, relativePath);
                return -1;
            }

            var data = memory.ReadString(dataPtr, dataLen, Encoding.UTF8);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, data);
            return 0;
        });
    }

    private void RegisterHttpRequest(Linker linker, string pluginName)
    {
        linker.DefineFunction("env", "http_request", (Caller caller, int reqPtr, int reqLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null || _httpClientFactory is null) return 0;

            var requestJson = memory.ReadString(reqPtr, reqLen, Encoding.UTF8);
            var request = JsonSerializer.Deserialize<HostHttpRequest>(requestJson);
            if (request is null) return 0;

            var client = _httpClientFactory.CreateClient($"wasm-plugin-{pluginName}");
            var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            if (request.Body is not null)
                httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");

            if (request.Headers is not null)
            {
                foreach (var (key, value) in request.Headers)
                    httpRequest.Headers.TryAddWithoutValidation(key, value);
            }

            var response = client.Send(httpRequest);
            var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var result = JsonSerializer.Serialize(new { status = (int)response.StatusCode, body = responseBody });
            return WriteStringToWasm(caller, result);
        });
    }

    private void RegisterMcpCall(Linker linker, string pluginName)
    {
        linker.DefineFunction("env", "mcp_call", (Caller caller, int reqPtr, int reqLen) =>
        {
            var memory = caller.GetMemory("memory");
            if (memory is null || _mcpService is null) return 0;

            var requestJson = memory.ReadString(reqPtr, reqLen, Encoding.UTF8);
            var request = JsonSerializer.Deserialize<HostMcpRequest>(requestJson);
            if (request is null) return 0;

            var result = _mcpService.InvokeToolAsync(request.Server, request.Tool, request.Arguments)
                .GetAwaiter().GetResult();

            return WriteStringToWasm(caller, result);
        });
    }

    /// <summary>
    /// Writes a string into WASM memory using the module's allocate export.
    /// Returns the pointer to the allocated string.
    /// </summary>
    private static int WriteStringToWasm(Caller caller, string value)
    {
        var memory = caller.GetMemory("memory");
        var allocate = caller.GetFunction("allocate");
        if (memory is null || allocate is null) return 0;

        var bytes = Encoding.UTF8.GetBytes(value);
        var ptr = (int)allocate.Invoke(bytes.Length)!;
        bytes.CopyTo(memory.GetSpan(ptr, bytes.Length));
        return ptr;
    }
}

internal sealed class HostHttpRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public string? Body { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

internal sealed class HostMcpRequest
{
    public string Server { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{}";
}
