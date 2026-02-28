using Gantri.Plugins.Sdk;

namespace FileDelete.Plugin;

public sealed class DeleteAction : ISdkPluginAction
{
    public string ActionName => "delete";
    public string Description => "Delete a file";

    public Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("path", out var pathObj) || pathObj is not string path || string.IsNullOrWhiteSpace(path))
            return Task.FromResult(ActionResult.Fail("Missing required parameter: path"));

        path = PathSecurity.ResolvePath(path, context.WorkingDirectory);

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory) && !PathSecurity.IsPathWithinDirectory(path, context.WorkingDirectory))
            return Task.FromResult(ActionResult.Fail($"Path is outside working directory: {path}"));

        if (!File.Exists(path))
            return Task.FromResult(ActionResult.Fail($"File not found: {path}"));

        File.Delete(path);
        return Task.FromResult(ActionResult.Ok($"Deleted: {path}"));
    }
}
