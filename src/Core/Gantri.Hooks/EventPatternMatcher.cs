namespace Gantri.Hooks;

public static class EventPatternMatcher
{
    /// <summary>
    /// Matches an event pattern against a filter pattern.
    /// Both use the format {domain}:{component}:{action}:{timing}.
    /// The filter pattern may use * as a wildcard for any single segment.
    /// </summary>
    public static bool Matches(string eventPattern, string filterPattern)
    {
        var eventParts = eventPattern.Split(':');
        var filterParts = filterPattern.Split(':');

        if (eventParts.Length != filterParts.Length)
            return false;

        for (var i = 0; i < eventParts.Length; i++)
        {
            if (filterParts[i] == "*")
                continue;

            if (!string.Equals(eventParts[i], filterParts[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }
}
