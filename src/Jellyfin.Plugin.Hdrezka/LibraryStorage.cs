namespace Jellyfin.Plugin.Hdrezka;

public static class LibraryStorage
{
    public static string ResolveRoot(string programDataPath, string? configuredRoot, IEnumerable<string>? existingLocations = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            if (!Path.IsPathFullyQualified(configuredRoot)) throw new ArgumentException("Library path must be absolute.", nameof(configuredRoot));
            return Path.GetFullPath(configuredRoot);
        }
        var data = Path.GetFullPath(programDataPath);
        var legacy = Path.Combine(Directory.GetParent(data)?.FullName ?? data, "hdrezka-library");
        var compare = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var legacyPaths = new[] { "films", "series", "cartoons", "animation", "show" }.Select(x => Path.GetFullPath(Path.Combine(legacy, x))).ToHashSet(compare);
        if (existingLocations is not null && existingLocations.Any(x => !string.IsNullOrWhiteSpace(x) && legacyPaths.Contains(Path.GetFullPath(x)))) return legacy;
        return Path.Combine(data, "hdrezka-library");
    }
}
