using System.Text.Json;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Mcp;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge;

/// <summary>
/// A real <see cref="AIFunction"/> wrapping <see cref="IMcpToolProvider.InvokeToolAsync"/>.
/// AF calls <see cref="InvokeCoreAsync"/> directly during tool execution.
/// </summary>
public sealed class McpToolFunction : AIFunction
{
    private readonly string _serverName;
    private readonly string _toolName;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;

    public McpToolFunction(
        string serverName,
        string toolName,
        string? description,
        JsonElement? inputSchema,
        IMcpToolProvider mcpToolProvider,
        IToolApprovalHandler? approvalHandler = null)
    {
        _serverName = serverName;
        _toolName = toolName;
        _mcpToolProvider = mcpToolProvider;
        _approvalHandler = approvalHandler;
        _name = $"{serverName}.{toolName}";
        _description = description ?? string.Empty;

        _jsonSchema = inputSchema.HasValue
            ? ToolFunctionHelpers.BuildFunctionSchema(_name, _description, inputSchema.Value)
            : default;
    }

    public override string Name => _name;
    public override string Description => _description;
    public override JsonElement JsonSchema => _jsonSchema;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var parameters = ToolFunctionHelpers.NormalizeArguments(arguments);

        if (_approvalHandler is not null)
        {
            var approval = await _approvalHandler.RequestApprovalAsync(Name, parameters, cancellationToken);
            if (!approval.Approved)
                return $"Tool call rejected: {approval.Reason ?? "User denied"}";
        }

        var result = await _mcpToolProvider.InvokeToolAsync(
            _serverName, _toolName, parameters, cancellationToken);

        return result.Success
            ? result.Content?.ToString() ?? string.Empty
            : $"Error: {result.Error ?? "MCP tool failed"}";
    }

}
