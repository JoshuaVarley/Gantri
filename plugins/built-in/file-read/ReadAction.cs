using Gantri.Plugins.Sdk;

namespace FileRead.Plugin;

public sealed class ReadAction : ISdkPluginAction
{
    public string ActionName => "read";
    public string Description => "Read file contents with line numbers, supporting offset and limit";

    public Task<ActionResult> ExecuteAsync(
        ActionContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !context.Parameters.TryGetValue("path", out var pathObj)
            || pathObj is not string path
            || string.IsNullOrWhiteSpace(path)
        )
            return Task.FromResult(ActionResult.Fail("Missing required parameter: path"));

        path = PathSecurity.ResolvePath(path, context.WorkingDirectory);

        if (
            !string.IsNullOrWhiteSpace(context.WorkingDirectory)
            && !PathSecurity.IsPathWithinDirectory(path, context.WorkingDirectory)
        )
            return Task.FromResult(
                ActionResult.Fail($"Path is outside working directory: {path}")
            );

        if (!File.Exists(path))
            return Task.FromResult(ActionResult.Fail($"File not found: {path}"));

        var offset = 1;
        if (context.Parameters.TryGetValue("offset", out var offsetObj))
        {
            offset = offsetObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 1,
            };
        }
        if (offset < 1)
            offset = 1;

        var limit = 500;
        if (context.Parameters.TryGetValue("limit", out var limitObj))
        {
            limit = limitObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 500,
            };
        }
        if (limit < 1)
            limit = 500;

        var allLines = File.ReadAllLines(path);
        var totalLines = allLines.Length;
        var startIndex = offset - 1;
        if (startIndex >= totalLines)
            return Task.FromResult(
                ActionResult.Ok(
                    $"[{path}] {totalLines} lines total\n(offset {offset} is beyond end of file)"
                )
            );

        var endIndex = Math.Min(startIndex + limit, totalLines);
        var lineNumberWidth = endIndex.ToString().Length;

        var lines = new System.Text.StringBuilder();
        lines.AppendLine(
            $"[{Path.GetFileName(path)}] {totalLines} lines total (showing lines {offset}-{endIndex})"
        );
        for (var i = startIndex; i < endIndex; i++)
        {
            lines.AppendLine($"{(i + 1).ToString().PadLeft(lineNumberWidth)}\t{allLines[i]}");
        }

        return Task.FromResult(ActionResult.Ok(lines.ToString()));
    }
}
