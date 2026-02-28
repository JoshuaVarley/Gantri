using System.Text.Json;
using Gantri.Plugins.Sdk;

namespace GitOperations.Plugin;

public sealed class CommitAction : ISdkPluginAction
{
    public string ActionName => "commit";
    public string Description => "Stage files and create a commit";

    private static readonly string[] ForbiddenPatterns = [".env", "*.key", "credentials*"];

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("message", out var msgObj) || msgObj is not string message || string.IsNullOrWhiteSpace(message))
            return ActionResult.Fail("Missing required parameter: message");

        var directory = GitHelper.ResolveDirectory(context);
        if (string.IsNullOrEmpty(directory))
            return ActionResult.Fail("No directory specified and no working directory configured");

        // Extract paths to stage
        var paths = new List<string>();
        if (context.Parameters.TryGetValue("paths", out var pathsObj) && pathsObj is not null)
        {
            if (pathsObj is IEnumerable<object?> enumerable)
                paths.AddRange(enumerable.Where(p => p is not null).Select(p => p!.ToString()!));
            else if (pathsObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
                paths.AddRange(jsonElement.EnumerateArray().Select(e => e.GetString()!).Where(s => s is not null));
        }

        // Safety: reject forbidden file patterns
        foreach (var path in paths)
        {
            foreach (var pattern in ForbiddenPatterns)
            {
                if (MatchesForbiddenPattern(path, pattern))
                    return ActionResult.Fail($"Refusing to commit file matching forbidden pattern '{pattern}': {path}");
            }
        }

        // Stage files
        if (paths.Count > 0)
        {
            var stageArgs = $"add {string.Join(' ', paths.Select(p => $"\"{p}\""))}";
            var (stageExit, _, stageError) = await GitHelper.RunGitAsync(stageArgs, directory, cancellationToken);
            if (stageExit != 0)
                return ActionResult.Fail($"git add failed: {stageError}");
        }
        else
        {
            var (stageExit, _, stageError) = await GitHelper.RunGitAsync("add -A", directory, cancellationToken);
            if (stageExit != 0)
                return ActionResult.Fail($"git add failed: {stageError}");
        }

        // Commit
        var escapedMessage = message.Replace("\"", "\\\"");
        var (exitCode, output, error) = await GitHelper.RunGitAsync($"commit -m \"{escapedMessage}\"", directory, cancellationToken);
        if (exitCode != 0)
            return ActionResult.Fail($"git commit failed: {error}");

        // Get the hash
        var (_, hash, _) = await GitHelper.RunGitAsync("rev-parse --short HEAD", directory, cancellationToken);

        return ActionResult.Ok($"Committed: {hash.Trim()}\n{output}");
    }

    private static bool MatchesForbiddenPattern(string path, string pattern)
    {
        var fileName = Path.GetFileName(path);

        if (pattern.StartsWith('*'))
        {
            var suffix = pattern[1..];
            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        if (pattern.EndsWith('*'))
        {
            var prefix = pattern[..^1];
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }
}
