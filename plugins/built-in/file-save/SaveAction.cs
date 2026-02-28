using Gantri.Plugins.Sdk;

namespace FileSave.Plugin;

public sealed class SaveAction : ISdkPluginAction
{
    public string ActionName => "save";
    public string Description => "Write content to a file path, creating directories as needed";

    public async Task<ActionResult> ExecuteAsync(
        ActionContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (
            !context.Parameters.TryGetValue("path", out var pathObj)
            || pathObj is not string path
            || string.IsNullOrWhiteSpace(path)
        )
            return ActionResult.Fail("Missing required parameter: path");

        if (
            !context.Parameters.TryGetValue("content", out var contentObj)
            || contentObj is not string content
        )
            return ActionResult.Fail("Missing required parameter: content");

        path = PathSecurity.ResolvePath(path, context.WorkingDirectory);

        if (
            !string.IsNullOrWhiteSpace(context.WorkingDirectory)
            && !PathSecurity.IsPathWithinDirectory(path, context.WorkingDirectory)
        )
        {
            return ActionResult.Fail($"Path is outside working directory: {path}");
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(path, content, cancellationToken);

        return ActionResult.Ok($"Saved {content.Length} characters to {path}");
    }
}
