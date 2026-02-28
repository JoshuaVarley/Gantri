using Gantri.Plugins.Sdk;

namespace GitOperations.Plugin;

public sealed class LogAction : ISdkPluginAction
{
    public string ActionName => "log";
    public string Description => "Show git commit log";

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        var directory = GitHelper.ResolveDirectory(context);
        if (string.IsNullOrEmpty(directory))
            return ActionResult.Fail("No directory specified and no working directory configured");

        var count = 10;
        if (context.Parameters.TryGetValue("count", out var countObj))
        {
            count = countObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 10
            };
        }

        var oneline = true;
        if (context.Parameters.TryGetValue("oneline", out var onelineObj))
        {
            oneline = onelineObj switch
            {
                bool b => b,
                string s => !s.Equals("false", StringComparison.OrdinalIgnoreCase),
                _ => true
            };
        }

        var format = oneline ? "--oneline" : "--format=medium";
        var (exitCode, output, error) = await GitHelper.RunGitAsync($"log -{count} {format}", directory, cancellationToken);
        if (exitCode != 0)
            return ActionResult.Fail($"git log failed: {error}");

        return ActionResult.Ok(string.IsNullOrEmpty(output) ? "No commits found" : output);
    }
}
