using Gantri.Plugins.Sdk;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace FileGlob.Plugin;

public sealed class SearchAction : ISdkPluginAction
{
    public string ActionName => "search";
    public string Description =>
        "Search for files matching a glob pattern, optionally filtering by content";

    public Task<ActionResult> ExecuteAsync(
        ActionContext context,
        CancellationToken cancellationToken = default
    )
    {
        string? directory = null;
        if (
            context.Parameters.TryGetValue("directory", out var dirObj)
            && dirObj is string dir
            && !string.IsNullOrWhiteSpace(dir)
        )
            directory = dir;

        // Fall back to working directory when no directory parameter supplied
        directory = PathSecurity.ResolveOptionalPath(directory, context.WorkingDirectory);

        if (string.IsNullOrEmpty(directory))
            return Task.FromResult(
                ActionResult.Fail(
                    "Missing required parameter: directory (and no working directory configured)"
                )
            );

        if (
            !string.IsNullOrWhiteSpace(context.WorkingDirectory)
            && !PathSecurity.IsPathWithinDirectory(directory, context.WorkingDirectory)
        )
        {
            return Task.FromResult(
                ActionResult.Fail($"Directory is outside working directory: {directory}")
            );
        }

        if (!Directory.Exists(directory))
            return Task.FromResult(ActionResult.Fail($"Directory not found: {directory}"));

        var pattern =
            context.Parameters.TryGetValue("pattern", out var patObj)
            && patObj is string pat
            && !string.IsNullOrWhiteSpace(pat)
                ? pat
                : "*.md";

        var searchTerm =
            context.Parameters.TryGetValue("search_term", out var termObj) && termObj is string term
                ? term
                : null;

        var matcher = new Matcher();
        foreach (var expanded in ExpandBraces(pattern))
            matcher.AddInclude(expanded);

        var dirInfo = new DirectoryInfoWrapper(new DirectoryInfo(directory));
        var matchResult = matcher.Execute(dirInfo);

        if (!matchResult.HasMatches)
            return Task.FromResult(ActionResult.Ok("No matches found"));

        var results = new List<string>();

        foreach (var file in matchResult.Files)
        {
            var fullPath = Path.GetFullPath(Path.Combine(directory, file.Path));

            if (string.IsNullOrEmpty(searchTerm))
            {
                results.Add(fullPath);
                continue;
            }

            var lines = File.ReadAllLines(fullPath);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add($"{fullPath}:{i + 1}: {lines[i]}");
                }
            }
        }

        if (results.Count == 0)
            return Task.FromResult(ActionResult.Ok("No matches found"));

        return Task.FromResult(ActionResult.Ok(string.Join(Environment.NewLine, results)));
    }

    public static List<string> ExpandBraces(string pattern)
    {
        var results = new List<string> { pattern };

        bool expanded;
        do
        {
            expanded = false;
            var next = new List<string>();

            foreach (var current in results)
            {
                var open = current.IndexOf('{');
                if (open < 0)
                {
                    next.Add(current);
                    continue;
                }

                var close = current.IndexOf('}', open);
                if (close < 0)
                {
                    next.Add(current);
                    continue;
                }

                expanded = true;
                var prefix = current[..open];
                var suffix = current[(close + 1)..];
                var alternatives = current[(open + 1)..close].Split(',');

                foreach (var alt in alternatives)
                    next.Add(prefix + alt + suffix);
            }

            results = next;
        } while (expanded);

        return results;
    }
}
