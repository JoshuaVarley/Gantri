using System.Collections.Concurrent;

namespace Gantri.Abstractions.Hooks;

public sealed class HookContext
{
    private readonly ConcurrentDictionary<string, object?> _properties = new();

    public HookEvent Event { get; }
    public CancellationToken CancellationToken { get; }
    public bool IsCancelled { get; private set; }
    public string? CancellationReason { get; private set; }
    public Exception? Error { get; set; }
    public object? Result { get; set; }

    public HookContext(HookEvent hookEvent, CancellationToken cancellationToken = default)
    {
        Event = hookEvent;
        CancellationToken = cancellationToken;
    }

    public void Cancel(string? reason = null)
    {
        IsCancelled = true;
        CancellationReason = reason;
    }

    public T? Get<T>(string key)
    {
        return _properties.TryGetValue(key, out var value) && value is T typed ? typed : default;
    }

    public void Set<T>(string key, T value)
    {
        _properties[key] = value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_properties.TryGetValue(key, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public bool Has(string key) => _properties.ContainsKey(key);

    public IReadOnlyDictionary<string, object?> Properties =>
        _properties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
}
