using System.Diagnostics;
using Gantri.Plugins.Sdk;

namespace GitOperations.Plugin;

internal static class GitHelper
{
    public static async Task<(int ExitCode, string Output, string Error)> RunGitAsync(
        string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
    }

    public static string? ResolveDirectory(ActionContext context)
    {
        string? directory = null;
        if (context.Parameters.TryGetValue("directory", out var dirObj) && dirObj is string dir && !string.IsNullOrWhiteSpace(dir))
            directory = dir;

        return PathSecurity.ResolveOptionalPath(directory, context.WorkingDirectory);
    }
}
