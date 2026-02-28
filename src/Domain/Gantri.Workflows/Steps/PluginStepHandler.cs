using Gantri.Abstractions.Configuration;
using Gantri.Abstractions.Plugins;

namespace Gantri.Workflows.Steps;

/// <summary>
/// Executes a workflow step by calling a plugin action.
/// </summary>
public sealed class PluginStepHandler : IStepHandler
{
    private readonly IPluginRouter _pluginRouter;

    public string StepType => "plugin";

    public PluginStepHandler(IPluginRouter pluginRouter)
    {
        _pluginRouter = pluginRouter;
    }

    public async Task<StepResult> ExecuteAsync(WorkflowStepDefinition step, WorkflowContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(step.Plugin))
            return StepResult.Fail($"Step '{step.Id}' is type 'plugin' but has no plugin specified.");

        var actionName = step.Action ?? step.Id;

        // Resolve parameter templates
        var resolvedParams = new Dictionary<string, object?>();
        foreach (var (key, value) in step.Parameters)
        {
            resolvedParams[key] = value is string s ? context.ResolveTemplate(s) : value;
        }

        try
        {
            var plugin = await _pluginRouter.ResolveAsync(step.Plugin, cancellationToken);
            var result = await plugin.ExecuteActionAsync(actionName, new PluginActionInput
            {
                ActionName = actionName,
                Parameters = resolvedParams
            }, cancellationToken);

            return result.Success
                ? StepResult.Ok(result.Output)
                : StepResult.Fail(result.Error ?? "Plugin action failed");
        }
        catch (Exception ex)
        {
            return StepResult.Fail($"Plugin '{step.Plugin}:{actionName}' failed: {ex.Message}");
        }
    }
}
