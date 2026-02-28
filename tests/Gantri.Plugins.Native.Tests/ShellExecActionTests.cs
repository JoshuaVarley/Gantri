using ShellExec.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class ShellExecActionTests
{
    private readonly RunAction _action = new();

    [Fact]
    public async Task Run_SimpleCommand_ReturnsOutput()
    {
        var command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello";
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "run",
            Parameters = new Dictionary<string, object?> { ["command"] = command }
        });

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("hello");
        output.Should().Contain("exit_code");
    }

    [Fact]
    public async Task Run_MissingCommand_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "run",
            Parameters = new Dictionary<string, object?>()
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("command");
    }

    [Fact]
    public async Task Run_AllowedCommand_Succeeds()
    {
        var command = OperatingSystem.IsWindows() ? "echo test" : "echo test";
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "run",
            Parameters = new Dictionary<string, object?>
            {
                ["command"] = command,
                ["__allowed_commands"] = new List<string> { "echo*" }
            }
        });

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Run_BlockedCommand_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "run",
            Parameters = new Dictionary<string, object?>
            {
                ["command"] = "rm -rf /",
                ["__allowed_commands"] = new List<string> { "echo*", "dotnet test*" }
            }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not in allowed list");
    }

    [Fact]
    public void IsCommandAllowed_WildcardMatch_ReturnsTrue()
    {
        RunAction.IsCommandAllowed("dotnet test --filter X", ["dotnet test*"]).Should().BeTrue();
        RunAction.IsCommandAllowed("dotnet build", ["dotnet *"]).Should().BeTrue();
    }

    [Fact]
    public void IsCommandAllowed_ExactMatch_ReturnsTrue()
    {
        RunAction.IsCommandAllowed("npm test", ["npm test"]).Should().BeTrue();
    }

    [Fact]
    public void IsCommandAllowed_NoMatch_ReturnsFalse()
    {
        RunAction.IsCommandAllowed("rm -rf /", ["dotnet *", "npm test"]).Should().BeFalse();
    }

    [Fact]
    public async Task Run_Timeout_ReturnsFailure()
    {
        // Use a command that sleeps longer than timeout
        var command = OperatingSystem.IsWindows() ? "ping -n 10 127.0.0.1" : "sleep 10";
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "run",
            Parameters = new Dictionary<string, object?>
            {
                ["command"] = command,
                ["timeout_seconds"] = 1
            }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("timed out");
    }
}
