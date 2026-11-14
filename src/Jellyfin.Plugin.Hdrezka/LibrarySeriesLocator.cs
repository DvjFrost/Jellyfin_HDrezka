using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public static class LibrarySeriesLocator
{
    public static LibrarySeriesRecord? Load(string root, string directory)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(directory));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length != 2 || parts[0] is not ("series" or "cartoons" or "animation" or "show")) return null;
        var file = Path.Combine(directory, "hdrezka-series.json");
        if (!File.Exists(file)) return null;
        var record = JsonSerializer.Deserialize<LibrarySeriesRecord>(File.ReadAllText(file));
        return record is null || CatalogIdentity.FromPath(record.Path) != record.Id || parts[1] != LibraryPath.TitleDirectory(record.Id) ? null : record;
    }
}
