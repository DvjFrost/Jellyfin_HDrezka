using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record LibraryVideoRecord(string Id, string Path, string? Season, string? Number, IReadOnlyList<FreeVoice> Voices);

public static class LibraryRecordLocator
{
    public static LibraryVideoRecord? Load(string root, string itemPath)
    {
        var absoluteRoot = Path.GetFullPath(root);
        var absoluteItem = Path.GetFullPath(itemPath);
        var relative = Path.GetRelativePath(absoluteRoot, absoluteItem);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) || !absoluteItem.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(absoluteItem)!;
        var movieJson = Path.Combine(dir, "hdrezka.json");
        if (File.Exists(movieJson))
        {
            var movie = JsonSerializer.Deserialize<LibraryMovieRecord>(File.ReadAllText(movieJson));
            return movie is null || CatalogIdentity.FromPath(movie.Path) != movie.Id ? null : new(movie.Id, movie.Path, null, null, movie.Voices);
        }
        var baseName = Path.GetFileNameWithoutExtension(absoluteItem);
        baseName = Regex.Replace(baseName, @" - voice-[0-9]{1,16}$", "", RegexOptions.CultureInvariant);
        var episodeJson = Path.Combine(dir, baseName + ".hdrezka.json");
        if (!File.Exists(episodeJson)) return null;
        var episode = JsonSerializer.Deserialize<LibraryEpisodeRecord>(File.ReadAllText(episodeJson));
        return episode is null || CatalogIdentity.FromPath(episode.Path) != episode.Id ? null : new(episode.Id, episode.Path, episode.Season, episode.Number, episode.Voices);
    }
}
