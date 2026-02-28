namespace Gantri.Abstractions.Plugins;

public interface IPluginAction
{
    string ActionName { get; }
    string Description { get; }
    Task<PluginActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken = default);
}
