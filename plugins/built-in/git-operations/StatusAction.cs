using Gantri.Plugins.Sdk;

namespace GitOperations.Plugin;

public sealed class StatusAction : ISdkPluginAction
{
    public string ActionName => "status";
    public string Description => "Show git working tree status";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var directory = GitHelper.ResolveDirectory(context);
        if (string.IsNullOrEmpty(directory))
            return ActionResult.Fail("No directory specified and no working directory configured");

        var (exitCode, output, error) = await GitHelper.RunGitAsync("status --porcelain", directory, cancellationToken);
        if (exitCode != 0)
            return ActionResult.Fail($"git status failed: {error}");

        return ActionResult.Ok(string.IsNullOrEmpty(output) ? "Working tree clean" : output);
    }
}
