using Gantri.Abstractions.Hooks;

namespace Gantri.Hooks.Tests;

public class TestHook : IHook
{
    private readonly Func<HookContext, Func<ValueTask>?, ValueTask>? _execute;

    public TestHook(string name, string eventPattern, HookTiming timing, int priority = 500,
        Func<HookContext, Func<ValueTask>?, ValueTask>? execute = null)
    {
        Name = name;
        EventPattern = eventPattern;
        Timing = timing;
        Priority = priority;
        _execute = execute;
    }

    public string Name { get; }
    public string EventPattern { get; }
    public int Priority { get; }
    public HookTiming Timing { get; }
    public int ExecutionCount { get; private set; }

    public async ValueTask ExecuteAsync(HookContext context, Func<ValueTask>? next = null)
    {
        ExecutionCount++;
        if (_execute is not null)
            await _execute(context, next);
        else if (next is not null)
            await next();
    }
}
