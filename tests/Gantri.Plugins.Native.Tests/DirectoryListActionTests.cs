using DirectoryList.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class DirectoryListActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TreeAction _action = new();

    public DirectoryListActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-tree-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# Readme");
        File.WriteAllText(Path.Combine(_tempDir, "data.txt"), "Some data");
        File.WriteAllText(Path.Combine(_tempDir, ".hidden"), "hidden file");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        File.WriteAllText(Path.Combine(_tempDir, "src", "main.cs"), "class Main {}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "src", "nested"));
        File.WriteAllText(
            Path.Combine(_tempDir, "src", "nested", "deep.cs"),
            "class Deep {}"
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Tree_DefaultDepth_ShowsStructure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("src/");
        output.Should().Contain("readme.md");
        output.Should().Contain("data.txt");
    }

    [Fact]
    public async Task Tree_DepthOne_DoesNotShowNested()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["depth"] = 1,
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("src/");
        output.Should().NotContain("main.cs");
    }

    [Fact]
    public async Task Tree_ExcludesHiddenByDefault()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotContain(".hidden");
    }

    [Fact]
    public async Task Tree_IncludeHidden_ShowsHiddenFiles()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["include_hidden"] = true,
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain(".hidden");
    }

    [Fact]
    public async Task Tree_WithPattern_FiltersFiles()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.md",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("readme.md");
        output.Should().NotContain("data.txt");
    }

    [Fact]
    public async Task Tree_NonexistentDirectory_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = Path.Combine(_tempDir, "nope"),
                },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Tree_OutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideDir = Path.GetPathRoot(_tempDir) ?? _tempDir;

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = outsideDir,
                },
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Tree_NoDirectoryWithWorkingDirectory_UsesWorkingDirectory()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "tree",
                Parameters = new Dictionary<string, object?>(),
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("readme.md");
    }
}
