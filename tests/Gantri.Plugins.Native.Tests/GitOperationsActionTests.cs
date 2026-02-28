using GitOperations.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class GitOperationsActionTests : IDisposable
{
    private readonly string _tempDir;

    public GitOperationsActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Init a git repo
        RunGit("init");
        RunGit("config user.email \"test@test.com\"");
        RunGit("config user.name \"Test\"");
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# Test");
        RunGit("add .");
        RunGit("commit -m \"initial commit\"");
    }

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir)) return;

        // Git creates read-only files in .git/objects; clear the attribute before deleting
        foreach (var file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }

        Directory.Delete(_tempDir, recursive: true);
    }

    private void RunGit(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = _tempDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
    }

    [Fact]
    public async Task Status_CleanRepo_ReturnsClean()
    {
        var action = new StatusAction();
        var result = await action.ExecuteAsync(new ActionContext
        {
            ActionName = "status",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("clean");
    }

    [Fact]
    public async Task Status_ModifiedFile_ShowsChanges()
    {
        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# Modified");

        var action = new StatusAction();
        var result = await action.ExecuteAsync(new ActionContext
        {
            ActionName = "status",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("readme.md");
    }

    [Fact]
    public async Task Log_ReturnsCommits()
    {
        var action = new LogAction();
        var result = await action.ExecuteAsync(new ActionContext
        {
            ActionName = "log",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("initial commit");
    }

    [Fact]
    public async Task Diff_NoChanges_ReturnsNoDifferences()
    {
        var action = new DiffAction();
        var result = await action.ExecuteAsync(new ActionContext
        {
            ActionName = "diff",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("No differences");
    }

    [Fact]
    public async Task Commit_ForbiddenEnvFile_ReturnsFailure()
    {
        var action = new CommitAction();
        var result = await action.ExecuteAsync(new ActionContext
        {
            ActionName = "commit",
            Parameters = new Dictionary<string, object?>
            {
                ["message"] = "bad commit",
                ["paths"] = new List<object?> { ".env" }
            },
            WorkingDirectory = _tempDir
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("forbidden");
    }
}
