namespace Gantri.Plugins.Sdk;

public interface ISdkPluginAction
{
    string ActionName { get; }
    string Description { get; }
    Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default);
}
