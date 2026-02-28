namespace Gantri.Abstractions.Hooks;

public sealed record HookEvent(
    string Domain,
    string Component,
    string Action,
    HookTiming Timing)
{
    public string Pattern => $"{Domain}:{Component}:{Action}:{Timing.ToString().ToLowerInvariant()}";

    public static HookEvent Parse(string pattern)
    {
        var parts = pattern.Split(':');
        if (parts.Length != 4)
            throw new ArgumentException($"Invalid hook event pattern '{pattern}'. Expected format: '{{domain}}:{{component}}:{{action}}:{{timing}}'.", nameof(pattern));

        if (!Enum.TryParse<HookTiming>(parts[3], ignoreCase: true, out var timing))
            throw new ArgumentException($"Invalid timing '{parts[3]}'. Expected one of: {string.Join(", ", Enum.GetNames<HookTiming>())}.", nameof(pattern));

        return new HookEvent(parts[0], parts[1], parts[2], timing);
    }
}
