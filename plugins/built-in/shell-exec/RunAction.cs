using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Gantri.Plugins.Sdk;

namespace ShellExec.Plugin;

public sealed class RunAction : ISdkPluginAction
{
    public string ActionName => "run";
    public string Description => "Execute a shell command and return its output";

    private const int MaxOutputBytes = 50 * 1024; // 50KB

    public async Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        if (!context.Parameters.TryGetValue("command", out var cmdObj) || cmdObj is not string command || string.IsNullOrWhiteSpace(command))
            return ActionResult.Fail("Missing required parameter: command");

        var timeoutSeconds = 120;
        if (context.Parameters.TryGetValue("timeout_seconds", out var timeoutObj))
        {
            timeoutSeconds = timeoutObj switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 120
            };
        }
        if (timeoutSeconds < 1) timeoutSeconds = 1;
        if (timeoutSeconds > 600) timeoutSeconds = 600;

        // Extract allowed commands list (framework-injected by GantriAgentFactory)
        var allowedCommands = ExtractAllowedCommands(context.Parameters);
        if (allowedCommands.Count > 0 && !IsCommandAllowed(command, allowedCommands))
            return ActionResult.Fail($"Command not in allowed list. Allowed patterns: {string.Join(", ", allowedCommands)}");

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/sh",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = context.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null && stdoutBuilder.Length < MaxOutputBytes)
                    stdoutBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null && stderrBuilder.Length < MaxOutputBytes)
                    stderrBuilder.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return ActionResult.Fail($"Command timed out after {timeoutSeconds} seconds");
            }

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();

            if (stdout.Length > MaxOutputBytes)
                stdout = stdout[..MaxOutputBytes] + "\n... [output truncated at 50KB]";
            if (stderr.Length > MaxOutputBytes)
                stderr = stderr[..MaxOutputBytes] + "\n... [output truncated at 50KB]";

            var result = new
            {
                exit_code = process.ExitCode,
                stdout = stdout.TrimEnd(),
                stderr = stderr.TrimEnd()
            };

            return ActionResult.Ok(JsonSerializer.Serialize(result));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ActionResult.Fail($"Failed to execute command: {ex.Message}");
        }
    }

    private static List<string> ExtractAllowedCommands(IReadOnlyDictionary<string, object?> parameters)
    {
        if (!parameters.TryGetValue("__allowed_commands", out var value) || value is null)
            return [];

        if (value is List<string> list)
            return list;

        if (value is IEnumerable<object?> enumerable)
            return enumerable.Where(x => x is not null).Select(x => x!.ToString()!).ToList();

        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            return jsonElement.EnumerateArray().Select(e => e.GetString()!).Where(s => s is not null).ToList();

        return [];
    }

    public static bool IsCommandAllowed(string command, IReadOnlyList<string> allowedPatterns)
    {
        var trimmedCommand = command.Trim();

        foreach (var pattern in allowedPatterns)
        {
            if (pattern.EndsWith('*'))
            {
                var prefix = pattern[..^1]; // Remove trailing *
                if (trimmedCommand.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                if (trimmedCommand.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }
}
