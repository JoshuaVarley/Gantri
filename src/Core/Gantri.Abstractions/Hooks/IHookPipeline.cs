namespace Gantri.Abstractions.Hooks;

public interface IHookPipeline
{
    ValueTask<HookContext> ExecuteAsync(
        string eventPattern,
        Func<HookContext, ValueTask> operation,
        HookContext? context = null,
        CancellationToken cancellationToken = default);

    ValueTask<HookContext> ExecuteAsync(
        HookEvent hookEvent,
        Func<HookContext, ValueTask> operation,
        HookContext? context = null,
        CancellationToken cancellationToken = default);
}
