using FileSave.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class FileSaveActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SaveAction _action = new();

    public FileSaveActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-save-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Save_ValidPathAndContent_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "test.md");
        var content = "# Hello World\nThis is a test.";

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["content"] = content,
                },
            }
        );

        result.Success.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(content);
    }

    [Fact]
    public async Task Save_MissingPath_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?> { ["content"] = "some content" },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path");
    }

    [Fact]
    public async Task Save_MissingContent_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = Path.Combine(_tempDir, "test.md"),
                },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content");
    }

    [Fact]
    public async Task Save_RelativePathWithWorkingDirectory_ResolvesAgainstIt()
    {
        var workDir = Path.Combine(_tempDir, "work");
        Directory.CreateDirectory(workDir);

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = "output.md",
                    ["content"] = "resolved content",
                },
                WorkingDirectory = workDir,
            }
        );

        result.Success.Should().BeTrue();
        var expected = Path.Combine(workDir, "output.md");
        File.Exists(expected).Should().BeTrue();
        (await File.ReadAllTextAsync(expected)).Should().Be("resolved content");
    }

    [Fact]
    public async Task Save_AbsolutePathOutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideRoot = Path.GetPathRoot(_tempDir) ?? _tempDir;
        var absolutePath = Path.Combine(outsideRoot, $"gantri-save-outside-{Guid.NewGuid():N}.md");
        var workDir = Path.Combine(_tempDir, "work");
        Directory.CreateDirectory(workDir);

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = absolutePath,
                    ["content"] = "absolute content",
                },
                WorkingDirectory = workDir,
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Save_NestedDirectory_CreatesDirectories()
    {
        var path = Path.Combine(_tempDir, "a", "b", "c", "deep.md");
        var content = "Nested content";

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "save",
                Parameters = new Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["content"] = content,
                },
            }
        );

        result.Success.Should().BeTrue();
        File.Exists(path).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(content);
    }
}
