using Gantri.Plugins.Sdk;

namespace GitOperations.Plugin;

public sealed class DiffAction : ISdkPluginAction
{
    public string ActionName => "diff";
    public string Description => "Show git diff";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var directory = GitHelper.ResolveDirectory(context);
        if (string.IsNullOrEmpty(directory))
            return ActionResult.Fail("No directory specified and no working directory configured");

        var args = "diff";

        var staged = false;
        if (context.Parameters.TryGetValue("staged", out var stagedObj))
        {
            staged = stagedObj switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }
        if (staged) args += " --staged";

        if (context.Parameters.TryGetValue("path", out var pathObj) && pathObj is string path && !string.IsNullOrWhiteSpace(path))
            args += $" -- {path}";

        var (exitCode, output, error) = await GitHelper.RunGitAsync(args, directory, cancellationToken);
        if (exitCode != 0)
            return ActionResult.Fail($"git diff failed: {error}");

        return ActionResult.Ok(string.IsNullOrEmpty(output) ? "No differences found" : output);
    }
}
