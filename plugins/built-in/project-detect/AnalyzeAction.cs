using System.Text.Json;
using Gantri.Plugins.Sdk;

namespace ProjectDetect.Plugin;

public sealed class AnalyzeAction : ISdkPluginAction
{
    public string ActionName => "analyze";
    public string Description => "Analyze a directory to detect project language, framework, and build tools";

    public Task<ActionResult> ExecuteAsync(ActionContext context, CancellationToken cancellationToken = default)
    {
        string? directory = null;
        if (context.Parameters.TryGetValue("directory", out var dirObj) && dirObj is string dir && !string.IsNullOrWhiteSpace(dir))
            directory = dir;

        directory = PathSecurity.ResolveOptionalPath(directory, context.WorkingDirectory);

        if (string.IsNullOrEmpty(directory))
            return Task.FromResult(ActionResult.Fail("No directory specified and no working directory configured"));

        if (!string.IsNullOrWhiteSpace(context.WorkingDirectory) && !PathSecurity.IsPathWithinDirectory(directory, context.WorkingDirectory))
            return Task.FromResult(ActionResult.Fail($"Directory is outside working directory: {directory}"));

        if (!Directory.Exists(directory))
            return Task.FromResult(ActionResult.Fail($"Directory not found: {directory}"));

        var result = DetectProject(directory);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        return Task.FromResult(ActionResult.Ok(json));
    }

    private static ProjectInfo DetectProject(string directory)
    {
        var info = new ProjectInfo();
        var files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
        var fileNames = files.Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // .NET / C#
        var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories);
        var slnFiles = Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly)).ToArray();

        if (csprojFiles.Length > 0 || slnFiles.Length > 0)
        {
            info.Language = "C#";
            info.Framework = ".NET";
            info.BuildCommand = "dotnet build";
            info.TestCommand = "dotnet test";
            info.PackageManager = "NuGet";
            info.EntryPoints = csprojFiles.Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).ToList();
            info.ConfigFiles = slnFiles.Select(Path.GetFileName).Where(f => f is not null).Select(f => f!).ToList();
            return info;
        }

        // Node.js / JavaScript / TypeScript
        if (fileNames.Contains("package.json"))
        {
            info.Language = fileNames.Contains("tsconfig.json") ? "TypeScript" : "JavaScript";
            info.Framework = "Node.js";
            info.PackageManager = fileNames.Contains("yarn.lock") ? "Yarn"
                : fileNames.Contains("pnpm-lock.yaml") ? "pnpm" : "npm";
            info.BuildCommand = $"{info.PackageManager.ToLowerInvariant()} run build";
            info.TestCommand = $"{info.PackageManager.ToLowerInvariant()} test";
            info.EntryPoints = ["package.json"];
            info.ConfigFiles = fileNames.Where(f => f.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) || f == "package.json").ToList();
            return info;
        }

        // Rust
        if (fileNames.Contains("Cargo.toml"))
        {
            info.Language = "Rust";
            info.Framework = "Cargo";
            info.BuildCommand = "cargo build";
            info.TestCommand = "cargo test";
            info.PackageManager = "Cargo";
            info.EntryPoints = ["Cargo.toml"];
            info.ConfigFiles = ["Cargo.toml"];
            if (fileNames.Contains("Cargo.lock")) info.ConfigFiles.Add("Cargo.lock");
            return info;
        }

        // Go
        if (fileNames.Contains("go.mod"))
        {
            info.Language = "Go";
            info.Framework = "Go Modules";
            info.BuildCommand = "go build ./...";
            info.TestCommand = "go test ./...";
            info.PackageManager = "Go Modules";
            info.EntryPoints = ["go.mod"];
            info.ConfigFiles = ["go.mod"];
            if (fileNames.Contains("go.sum")) info.ConfigFiles.Add("go.sum");
            return info;
        }

        // Java
        if (fileNames.Contains("pom.xml"))
        {
            info.Language = "Java";
            info.Framework = "Maven";
            info.BuildCommand = "mvn compile";
            info.TestCommand = "mvn test";
            info.PackageManager = "Maven";
            info.EntryPoints = ["pom.xml"];
            info.ConfigFiles = ["pom.xml"];
            return info;
        }

        if (fileNames.Contains("build.gradle") || fileNames.Contains("build.gradle.kts"))
        {
            info.Language = "Java";
            info.Framework = "Gradle";
            info.BuildCommand = "gradle build";
            info.TestCommand = "gradle test";
            info.PackageManager = "Gradle";
            info.EntryPoints = fileNames.Where(f => f.StartsWith("build.gradle", StringComparison.OrdinalIgnoreCase)).ToList();
            info.ConfigFiles = info.EntryPoints.ToList();
            return info;
        }

        // Python
        if (fileNames.Contains("pyproject.toml") || fileNames.Contains("requirements.txt") || fileNames.Contains("setup.py"))
        {
            info.Language = "Python";
            info.Framework = fileNames.Contains("pyproject.toml") ? "Poetry/PEP 517" : "pip";
            info.BuildCommand = fileNames.Contains("pyproject.toml") ? "python -m build" : "pip install .";
            info.TestCommand = "pytest";
            info.PackageManager = fileNames.Contains("pyproject.toml") ? "pip/poetry" : "pip";
            info.EntryPoints = fileNames.Where(f =>
                f.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase) ||
                f.Equals("setup.py", StringComparison.OrdinalIgnoreCase) ||
                f.Equals("requirements.txt", StringComparison.OrdinalIgnoreCase)
            ).ToList();
            info.ConfigFiles = info.EntryPoints.ToList();
            return info;
        }

        info.Language = "Unknown";
        return info;
    }

    private sealed class ProjectInfo
    {
        public string Language { get; set; } = "Unknown";
        public string? Framework { get; set; }
        public string? BuildCommand { get; set; }
        public string? TestCommand { get; set; }
        public string? PackageManager { get; set; }
        public List<string> EntryPoints { get; set; } = [];
        public List<string> ConfigFiles { get; set; } = [];
    }
}
