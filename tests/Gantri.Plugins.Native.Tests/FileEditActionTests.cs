using FileEdit.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class FileEditActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SearchReplaceAction _searchReplace = new();
    private readonly InsertAction _insert = new();

    public FileEditActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-edit-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task SearchReplace_FirstOccurrence_ReplacesOnlyFirst()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "hello world\nhello again\nhello three");

        var result = await _searchReplace.ExecuteAsync(new ActionContext
        {
            ActionName = "search-replace",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = filePath, ["search"] = "hello", ["replace"] = "goodbye"
            }
        });

        result.Success.Should().BeTrue();
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().StartWith("goodbye world");
        content.Should().Contain("hello again");
    }

    [Fact]
    public async Task SearchReplace_AllOccurrences_ReplacesAll()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "hello world\nhello again");

        var result = await _searchReplace.ExecuteAsync(new ActionContext
        {
            ActionName = "search-replace",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = filePath, ["search"] = "hello", ["replace"] = "goodbye", ["occurrence"] = 0
            }
        });

        result.Success.Should().BeTrue();
        var content = await File.ReadAllTextAsync(filePath);
        content.Should().Contain("goodbye world");
        content.Should().Contain("goodbye again");
        content.Should().NotContain("hello");
    }

    [Fact]
    public async Task SearchReplace_NotFound_ReturnsFailure()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "hello world");

        var result = await _searchReplace.ExecuteAsync(new ActionContext
        {
            ActionName = "search-replace",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = filePath, ["search"] = "xyz", ["replace"] = "abc"
            }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task SearchReplace_OutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideRoot = Path.GetPathRoot(_tempDir) ?? _tempDir;
        var absolutePath = Path.Combine(outsideRoot, $"gantri-edit-outside-{Guid.NewGuid():N}.txt");
        var workDir = Path.Combine(_tempDir, "work");
        Directory.CreateDirectory(workDir);

        var result = await _searchReplace.ExecuteAsync(new ActionContext
        {
            ActionName = "search-replace",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = absolutePath, ["search"] = "a", ["replace"] = "b"
            },
            WorkingDirectory = workDir
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Insert_ValidLine_InsertsContent()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "line 1\nline 2\nline 3");

        var result = await _insert.ExecuteAsync(new ActionContext
        {
            ActionName = "insert",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = filePath, ["line"] = 2, ["content"] = "inserted line"
            }
        });

        result.Success.Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(filePath);
        lines[0].Should().Be("line 1");
        lines[1].Should().Be("inserted line");
        lines[2].Should().Be("line 2");
    }

    [Fact]
    public async Task Insert_BeyondEndOfFile_ReturnsFailure()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "line 1\nline 2");

        var result = await _insert.ExecuteAsync(new ActionContext
        {
            ActionName = "insert",
            Parameters = new Dictionary<string, object?>
            {
                ["path"] = filePath, ["line"] = 100, ["content"] = "inserted"
            }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("beyond end of file");
    }
}
