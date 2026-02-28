using System.Collections.Concurrent;
using Gantri.Abstractions.Hooks;

namespace Gantri.Hooks;

public sealed class HookRegistry
{
    private readonly ConcurrentDictionary<string, IHook> _hooks = new();

    public void Register(IHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        _hooks[hook.Name] = hook;
    }

    public bool Deregister(string hookName)
    {
        return _hooks.TryRemove(hookName, out _);
    }

    public IReadOnlyList<IHook> GetMatchingHooks(string eventPattern, HookTiming? timing = null)
    {
        return _hooks.Values
            .Where(h => EventPatternMatcher.Matches(eventPattern, h.EventPattern))
            .Where(h => timing == null || h.Timing == timing)
            .OrderBy(h => h.Priority)
            .ToList();
    }

    public IReadOnlyList<IHook> GetAllHooks() => _hooks.Values.ToList();

    public int Count => _hooks.Count;
}
