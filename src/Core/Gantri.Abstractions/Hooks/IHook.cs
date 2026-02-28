namespace Gantri.Abstractions.Hooks;

public interface IHook
{
    string Name { get; }
    string EventPattern { get; }
    int Priority { get; }
    HookTiming Timing { get; }
    ValueTask ExecuteAsync(HookContext context, Func<ValueTask>? next = null);
}
