using Gantri.Abstractions.Plugins;
using Microsoft.Extensions.Logging;

namespace Gantri.Bridge;

/// <summary>
/// Custom action type for AF declarative YAML workflows.
/// Enables <c>kind: InvokeGantriPlugin</c> steps that call through to <see cref="IPluginRouter"/>.
/// </summary>
public sealed class InvokeGantriPluginAction
{
    private readonly IPluginRouter _pluginRouter;
    private readonly ILogger<InvokeGantriPluginAction> _logger;

    public InvokeGantriPluginAction(IPluginRouter pluginRouter, ILogger<InvokeGantriPluginAction> logger)
    {
        _pluginRouter = pluginRouter;
        _logger = logger;
    }

    public async Task<string> ExecuteAsync(
        string pluginName,
        string actionName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("InvokeGantriPlugin: {Plugin}.{Action}", pluginName, actionName);

        var plugin = await _pluginRouter.ResolveAsync(pluginName, cancellationToken);
        var result = await plugin.ExecuteActionAsync(actionName, new PluginActionInput
        {
            ActionName = actionName,
            Parameters = parameters ?? new Dictionary<string, object?>(),
            WorkingDirectory = workingDirectory
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Plugin action {Plugin}.{Action} failed: {Error}",
                pluginName, actionName, result.Error);
            throw new InvalidOperationException(
                $"Plugin action '{pluginName}.{actionName}' failed: {result.Error}");
        }

        return result.Output?.ToString() ?? string.Empty;
    }
}
