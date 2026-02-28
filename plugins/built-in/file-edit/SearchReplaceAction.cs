using Gantri.Plugins.Sdk;

namespace FileEdit.Plugin;

public sealed class SearchReplaceAction : ISdkPluginAction
{
    public string ActionName => "search-replace";
    public string Description => "Find and replace text in a file";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
            return ActionResult.Fail("Missing required parameter: path");
        if (!context.Parameters.TryGetValue("search", out var searchObj) || searchObj is not string search || string.IsNullOrEmpty(search))
            return ActionResult.Fail("Missing required parameter: search");
        if (!context.Parameters.TryGetValue("replace", out var replaceObj) || replaceObj is not string replace)
            return ActionResult.Fail("Missing required parameter: replace");

        path = PathSecurity.ResolvePath(path, context.WorkingDirectory);

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory) && !PathSecurity.IsPathWithinDirectory(path, context.WorkingDirectory))
            return ActionResult.Fail($"Path is outside working directory: {path}");

        if (!File.Exists(path))
            return ActionResult.Fail($"File not found: {path}");

        var occurrence = 1;
        if (context.Parameters.TryGetValue("occurrence", out var occObj))
        {
            occurrence = occObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 1
            };
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);

        if (!content.Contains(search))
            return ActionResult.Fail($"Search text not found in file: {path}");

        string newContent;
        int replacementCount;

        if (occurrence == 0)
        {
            // Replace all
            newContent = content.Replace(search, replace);
            replacementCount = (content.Length - newContent.Length + replace.Length * CountOccurrences(content, search)) > 0
                ? CountOccurrences(content, search) : 0;
        }
        else
        {
            // Replace specific occurrence
            var index = -1;
            for (var i = 0; i < occurrence; i++)
            {
                index = content.IndexOf(search, index + 1, StringComparison.Ordinal);
                if (index < 0)
                    return ActionResult.Fail($"Only found {i} occurrence(s) of search text, requested occurrence {occurrence}");
            }

            newContent = string.Concat(content.AsSpan(0, index), replace, content.AsSpan(index + search.Length));
            replacementCount = 1;
        }

        await File.WriteAllTextAsync(path, newContent, cancellationToken);

        // Build diff-style output
        var lines = newContent.Split('\n');
        var searchLineIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(replace))
            {
                searchLineIndex = i;
                break;
            }
        }

        var contextStart = Math.Max(0, searchLineIndex - 2);
        var contextEnd = Math.Min(lines.Length, searchLineIndex + 3);
        var preview = string.Join("\n", lines[contextStart..contextEnd]);

        return ActionResult.Ok($"Replaced {replacementCount} occurrence(s) in {Path.GetFileName(path)}\n\n{preview}");
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
