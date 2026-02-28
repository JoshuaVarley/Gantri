namespace Gantri.Plugins.Sdk;

public static class PathSecurity
{
    public static string ResolvePath(string path, string? workingDirectory)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);
        if (!string.IsNullOrEmpty(workingDirectory))
            return Path.GetFullPath(Path.Combine(workingDirectory, path));
        return Path.GetFullPath(path);
    }

    public static string? ResolveOptionalPath(string? path, string? workingDirectory)
    {
        if (path is null)
            return string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : Path.GetFullPath(workingDirectory);
        return ResolvePath(path, workingDirectory);
    }

    public static bool IsPathWithinDirectory(string path, string directory)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(fullDirectory, comparison)
            || fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, comparison);
    }
}
