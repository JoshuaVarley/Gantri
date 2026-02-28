using System.Text.RegularExpressions;
using Gantri.Plugins.Sdk;

namespace DirectoryList.Plugin;

public sealed class TreeAction : ISdkPluginAction
{
    public string ActionName => "tree";
    public string Description => "List directory contents in an indented tree format with file sizes";

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
            return Task.FromResult(
                ActionResult.Fail($"Directory is outside working directory: {directory}")
            );

        if (!Directory.Exists(directory))
            return Task.FromResult(ActionResult.Fail($"Directory not found: {directory}"));

        var depth = 3;
        if (context.Parameters.TryGetValue("depth", out var depthObj))
        {
            depth = depthObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 3,
            };
        }
        if (depth < 1)
            depth = 1;

        string? pattern = null;
        if (
            context.Parameters.TryGetValue("pattern", out var patObj)
            && patObj is string pat
            && !string.IsNullOrWhiteSpace(pat)
        )
            pattern = pat;

        var includeHidden = false;
        if (context.Parameters.TryGetValue("include_hidden", out var hiddenObj))
        {
            includeHidden = hiddenObj switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(directory);
        BuildTree(sb, directory, "", depth, pattern, includeHidden);

        return Task.FromResult(ActionResult.Ok(sb.ToString()));
    }

    private static void BuildTree(
        System.Text.StringBuilder sb,
        string directory,
        string indent,
        int remainingDepth,
        string? pattern,
        bool includeHidden
    )
    {
        if (remainingDepth <= 0)
            return;

        var entries = new List<FileSystemInfo>();

        try
        {
            var dirInfo = new DirectoryInfo(directory);
            entries.AddRange(dirInfo.GetDirectories());
            entries.AddRange(dirInfo.GetFiles());
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}[access denied]");
            return;
        }

        if (!includeHidden)
            entries = entries.Where(e => !e.Name.StartsWith('.')).ToList();

        entries.Sort(
            (a, b) =>
            {
                var aIsDir = a is DirectoryInfo;
                var bIsDir = b is DirectoryInfo;
                if (aIsDir != bIsDir)
                    return aIsDir ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            }
        );

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var isLast = i == entries.Count - 1;
            var connector = isLast ? "\u2514\u2500\u2500 " : "\u251c\u2500\u2500 ";
            var childIndent = indent + (isLast ? "    " : "\u2502   ");

            if (entry is DirectoryInfo subDir)
            {
                sb.AppendLine($"{indent}{connector}{subDir.Name}/");
                BuildTree(
                    sb,
                    subDir.FullName,
                    childIndent,
                    remainingDepth - 1,
                    pattern,
                    includeHidden
                );
            }
            else if (entry is FileInfo file)
            {
                if (pattern is not null && !MatchesPattern(file.Name, pattern))
                    continue;

                var size = FormatSize(file.Length);
                sb.AppendLine($"{indent}{connector}{file.Name} ({size})");
            }
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        var regexPattern =
            "^"
            + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")
            + "$";
        return Regex.IsMatch(fileName, regexPattern, RegexOptions.IgnoreCase);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }
}
