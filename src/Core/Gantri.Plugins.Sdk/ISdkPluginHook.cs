using Gantri.Abstractions.Hooks;

namespace Gantri.Plugins.Sdk;

public interface ISdkPluginHook
{
    string EventPattern { get; }
    int Priority { get; }
    Task ExecuteAsync(HookContext context, CancellationToken cancellationToken = default);
}
