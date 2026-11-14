namespace Jellyfin.Plugin.Hdrezka;

public static class MovieVersionPaths
{
    public static IReadOnlyList<string> Collect(string directory, string id, IReadOnlyList<FreeVoice> voices, IEnumerable<string> existing)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = new HashSet<string>(existing.Where(File.Exists), comparer);
        foreach (var voice in voices.Skip(1))
        {
            var path = Path.Combine(directory, LibraryPath.VersionFileName(id, voice.Id));
            if (File.Exists(path)) paths.Add(path);
        }
        return paths.Order(comparer).ToArray();
    }
}
