using FileGlob.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class FileGlobActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SearchAction _action = new();

    public FileGlobActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-glob-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Create test files
        File.WriteAllText(
            Path.Combine(_tempDir, "readme.md"),
            "# Readme\nThis is a readme file.\n"
        );
        File.WriteAllText(Path.Combine(_tempDir, "notes.md"), "# Notes\nImportant notes here.\n");
        File.WriteAllText(Path.Combine(_tempDir, "data.txt"), "Some text data.\n");
        File.WriteAllText(Path.Combine(_tempDir, "report.txt"), "Quarterly report.\n");

        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.md"), "# Nested\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Search_WithMatchingTerm_ReturnsFileLineContent()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.md",
                    ["search_term"] = "Important",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("notes.md");
        output.Should().Contain(":2:");
        output.Should().Contain("Important notes here.");
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsNoMatchesFound()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.md",
                    ["search_term"] = "nonexistent-term-xyz",
                },
            }
        );

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Be("No matches found");
    }

    [Fact]
    public async Task Search_MissingDirectory_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?> { ["pattern"] = "*.md" },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("directory");
    }

    [Fact]
    public async Task Search_NonexistentDirectory_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = Path.Combine(_tempDir, "does-not-exist"),
                    ["pattern"] = "*.md",
                },
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Search_NoDirectoryWithWorkingDirectory_UsesWorkingDirectory()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?> { ["pattern"] = "*.md" },
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("readme.md");
        output.Should().Contain("notes.md");
    }

    [Fact]
    public async Task Search_RelativeDirectoryWithWorkingDirectory_ResolvesAgainstIt()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = "sub",
                    ["pattern"] = "*.md",
                },
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("nested.md");
    }

    [Fact]
    public async Task Search_AbsoluteDirectoryOutsideWorkingDirectory_ReturnsFailure()
    {
        var outsideDir = Path.GetPathRoot(_tempDir) ?? _tempDir;

        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = outsideDir,
                    ["pattern"] = "*.md",
                },
                WorkingDirectory = _tempDir,
            }
        );

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside working directory");
    }

    [Fact]
    public async Task Search_GlobPatternFilters_ExcludesNonMatchingFiles()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.md",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("readme.md");
        output.Should().Contain("notes.md");
        output.Should().NotContain("data.txt");
    }

    [Fact]
    public async Task Search_BraceExpansion_MatchesMultipleExtensions()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.{md,txt}",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("readme.md");
        output.Should().Contain("notes.md");
        output.Should().Contain("data.txt");
        output.Should().Contain("report.txt");
    }

    [Fact]
    public async Task Search_BraceExpansion_MatchesInlineAlternatives()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*{readme,notes}*",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("readme.md");
        output.Should().Contain("notes.md");
        output.Should().NotContain("data.txt");
    }

    [Fact]
    public async Task Search_NoBraces_StillWorks()
    {
        var result = await _action.ExecuteAsync(
            new ActionContext
            {
                ActionName = "search",
                Parameters = new Dictionary<string, object?>
                {
                    ["directory"] = _tempDir,
                    ["pattern"] = "*.md",
                },
            }
        );

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().NotBeNull();
        output.Should().Contain("readme.md");
        output.Should().Contain("notes.md");
        output.Should().NotContain("data.txt");
    }

    [Fact]
    public void ExpandBraces_NoBraces_ReturnsSinglePattern()
    {
        var result = SearchAction.ExpandBraces("*.md");
        result.Should().BeEquivalentTo(["*.md"]);
    }

    [Fact]
    public void ExpandBraces_SingleGroup_ExpandsAlternatives()
    {
        var result = SearchAction.ExpandBraces("*.{md,txt}");
        result.Should().BeEquivalentTo(["*.md", "*.txt"]);
    }

    [Fact]
    public void ExpandBraces_InlineGroup_ExpandsAlternatives()
    {
        var result = SearchAction.ExpandBraces("**/*{news,News}*.md");
        result.Should().BeEquivalentTo(["**/*news*.md", "**/*News*.md"]);
    }
}
