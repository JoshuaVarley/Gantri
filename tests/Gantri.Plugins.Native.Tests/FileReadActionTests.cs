using FileRead.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class FileReadActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ReadAction _action = new();

    public FileReadActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-read-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var lines = Enumerable.Range(1, 20).Select(i => $"Line {i} content");
        File.WriteAllLines(Path.Combine(_tempDir, "test.txt"), lines);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Read_ValidFile_ReturnsLineNumberedContent()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = Path.Combine(_tempDir, "test.txt"),
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("20 lines total");
        output.Should().Contain("Line 1 content");
        output.Should().Contain("Line 20 content");
    }

    [Fact]
    public async Task Read_WithOffsetAndLimit_ReturnsSlice()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = Path.Combine(_tempDir, "test.txt"),
                    ["offset"] = 5,
                    ["limit"] = 3,
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("Line 5 content");
        output.Should().Contain("Line 7 content");
        output.Should().NotContain("Line 4 content");
        output.Should().NotContain("Line 8 content");
    }

    [Fact]
    public async Task Read_MissingFile_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = Path.Combine(_tempDir, "nonexistent.txt"),
                },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Read_MissingPath_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?>(),
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path");
    }

    [Fact]
    public async Task Read_OutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideRoot = Path.GetPathRoot(_tempDir) ?? _tempDir;
        var absolutePath = Path.Combine(
            outsideRoot,
            $"gantri-read-outside-{Guid.NewGuid():N}.txt"
        );
        var workDir = Path.Combine(_tempDir, "work");
        Directory.CreateDirectory(workDir);

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?> { ["path"] = absolutePath },
                WorkingDirectory = workDir,
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Read_RelativePathWithWorkingDirectory_ResolvesCorrectly()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "read",
                Parameters = new Dictionary<string, object?> { ["path"] = "test.txt" },
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("Line 1 content");
    }
}
