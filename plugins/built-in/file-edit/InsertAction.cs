using Gantri.Plugins.Sdk;

namespace FileEdit.Plugin;

public sealed class InsertAction : ISdkPluginAction
{
    public string ActionName => "insert";
    public string Description => "Insert text at a specific line number";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
            return ActionResult.Fail("Missing required parameter: path");

        if (!context.Parameters.TryGetValue("line", out var lineObj))
            return ActionResult.Fail("Missing required parameter: line");

        var lineNumber = lineObj switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => -1
        };

        if (lineNumber < 1)
            return ActionResult.Fail("Parameter 'line' must be a positive integer");

        if (!context.Parameters.TryGetValue("content", out var contentObj) || contentObj is not string content)
            return ActionResult.Fail("Missing required parameter: content");

        path = PathSecurity.ResolvePath(path, context.WorkingDirectory);

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory) && !PathSecurity.IsPathWithinDirectory(path, context.WorkingDirectory))
            return ActionResult.Fail($"Path is outside working directory: {path}");

        if (!File.Exists(path))
            return ActionResult.Fail($"File not found: {path}");

        var lines = (await File.ReadAllLinesAsync(path, cancellationToken)).ToList();

        if (lineNumber > lines.Count + 1)
            return ActionResult.Fail($"Line {lineNumber} is beyond end of file ({lines.Count} lines)");

        var insertLines = content.Split('\n');
        var insertIndex = lineNumber - 1;
        lines.InsertRange(insertIndex, insertLines);

        await File.WriteAllLinesAsync(path, lines, cancellationToken);

        // Show context around insertion
        var contextStart = Math.Max(0, insertIndex - 1);
        var contextEnd = Math.Min(lines.Count, insertIndex + insertLines.Length + 1);
        var lineWidth = contextEnd.ToString().Length;

        var preview = new System.Text.StringBuilder();
        preview.AppendLine($"Inserted {insertLines.Length} line(s) at line {lineNumber} in {Path.GetFileName(path)}");
        for (var i = contextStart; i < contextEnd; i++)
        {
            var marker = (i >= insertIndex && i < insertIndex + insertLines.Length) ? "+" : " ";
            preview.AppendLine($"{marker}{(i + 1).ToString().PadLeft(lineWidth)}\t{lines[i]}");
        }

        return ActionResult.Ok(preview.ToString());
    }
}
