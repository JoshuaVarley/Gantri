using ProjectDetect.Plugin;
using Gantri.Plugins.Sdk;

namespace Gantri.Plugins.Native.Tests;

public class ProjectDetectActionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AnalyzeAction _action = new();

    public ProjectDetectActionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"gantri-detect-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task Analyze_DotNetProject_DetectsCSharp()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Test.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("\"language\": \"C#\"");
        output.Should().Contain("dotnet build");
    }

    [Fact]
    public async Task Analyze_NodeProject_DetectsJavaScript()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        var output = result.Output as string;
        output.Should().Contain("\"language\": \"JavaScript\"");
    }

    [Fact]
    public async Task Analyze_TypeScriptProject_DetectsTypeScript()
    {
        File.WriteAllText(Path.Combine(_tempDir, "package.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "tsconfig.json"), "{}");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("\"language\": \"TypeScript\"");
    }

    [Fact]
    public async Task Analyze_RustProject_DetectsRust()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Cargo.toml"), "[package]");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("\"language\": \"Rust\"");
    }

    [Fact]
    public async Task Analyze_GoProject_DetectsGo()
    {
        File.WriteAllText(Path.Combine(_tempDir, "go.mod"), "module test");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("\"language\": \"Go\"");
    }

    [Fact]
    public async Task Analyze_PythonProject_DetectsPython()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pyproject.toml"), "[tool.pytest]");

        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("\"language\": \"Python\"");
    }

    [Fact]
    public async Task Analyze_EmptyDirectory_ReturnsUnknown()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = _tempDir }
        });

        result.Success.Should().BeTrue();
        (result.Output as string).Should().Contain("\"language\": \"Unknown\"");
    }

    [Fact]
    public async Task Analyze_NonexistentDirectory_ReturnsFailure()
    {
        var result = await _action.ExecuteAsync(new ActionContext
        {
            ActionName = "analyze",
            Parameters = new Dictionary<string, object?> { ["directory"] = Path.Combine(_tempDir, "nope") }
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }
}
