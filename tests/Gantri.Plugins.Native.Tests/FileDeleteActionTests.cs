using FileDelete.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class FileDeleteActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DeleteAction _action = new();

    public FileDeleteActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-delete-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Delete_ExistingFile_DeletesSuccessfully()
    {
        var filePath = Path.Combine(_tempDir, "to-delete.txt");
        await File.WriteAllTextAsync(filePath, "delete me");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "delete",
            Parameters = new Dictionary<string, object?> { ["path"] = filePath }
        });

        result.Success.Should().BeTrue();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_NonexistentFile_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "delete",
            Parameters = new Dictionary<string, object?> { ["path"] = Path.Combine(_tempDir, "nope.txt") }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Delete_OutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideRoot = Path.GetPathRoot(_tempDir) ?? _tempDir;
        var absolutePath = Path.Combine(outsideRoot, $"gantri-del-outside-{Guid.NewGuid():N}.txt");
        var workDir = Path.Combine(_tempDir, "work");
        Directory.CreateDirectory(workDir);

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "delete",
            Parameters = new Dictionary<string, object?> { ["path"] = absolutePath },
            WorkingDirectory = workDir
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Delete_MissingPath_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "delete",
            Parameters = new Dictionary<string, object?>()
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("path");
    }
}
