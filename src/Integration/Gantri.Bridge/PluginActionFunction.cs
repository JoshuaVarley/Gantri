using System.Text.Json;
using Gantri.Abstractions.Agents;
using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.AI;

namespace Gantri.Bridge;

/// <summary>
/// A real <see cref="AIFunction"/> that wraps plugin actions via <see cref="IPluginRouter"/>.
/// Unlike the old ProxyAIFunction (which threw on invoke), AF calls <see cref="InvokeCoreAsync"/> directly.
/// </summary>
public sealed class PluginActionFunction : AIFunction
{
    private readonly string _pluginName;
    private readonly string _actionName;
    private readonly IPluginRouter _pluginRouter;
    private readonly IToolApprovalHandler? _approvalHandler;
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _jsonSchema;
    private readonly string? _workingDirectory;
    private readonly IReadOnlyDictionary<string, object?>? _additionalParameters;

    public PluginActionFunction(
        string pluginName,
        string actionName,
        string? description,
        JsonElement? parametersSchema,
        IPluginRouter pluginRouter,
        string? workingDirectory = null,
        IToolApprovalHandler? approvalHandler = null,
        IReadOnlyDictionary<string, object?>? additionalParameters = null)
    {
        _pluginName = pluginName;
        _actionName = actionName;
        _pluginRouter = pluginRouter;
        _approvalHandler = approvalHandler;
        _name = $"{pluginName}.{actionName}";
        _description = description ?? string.Empty;
        _workingDirectory = workingDirectory;
        _additionalParameters = additionalParameters;

        _jsonSchema = parametersSchema.HasValue
            ? ToolFunctionHelpers.BuildFunctionSchema(_name, _description, parametersSchema.Value)
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

        // Merge framework-injected additional parameters (e.g., __allowed_commands)
        if (_additionalParameters is { Count: > 0 })
        {
            foreach (var kvp in _additionalParameters)
                parameters[kvp.Key] = kvp.Value;
        }

        if (_approvalHandler is not null)
        {
            var approval = await _approvalHandler.RequestApprovalAsync(Name, parameters, cancellationToken);
            if (!approval.Approved)
                return $"Tool call rejected: {approval.Reason ?? "User denied"}";
        }

        var plugin = await _pluginRouter.ResolveAsync(_pluginName, cancellationToken);
        var result = await plugin.ExecuteActionAsync(_actionName, new PluginActionInput
        {
            ActionName = _actionName,
            Parameters = parameters,
            WorkingDirectory = _workingDirectory
        }, cancellationToken);

        return result.Success
            ? result.Output?.ToString() ?? string.Empty
            : $"Error: {result.Error ?? "Plugin action failed"}";
    }

}
