using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record LibraryVideoRecord(string Id, string Path, string? Season, string? Number, IReadOnlyList<FreeVoice> Voices, bool MultipartMovie = false, bool UseMoviePlayer = false);

public static class LibraryRecordLocator
{
    public static LibraryVideoRecord? Load(string root, string itemPath)
    {
        var absoluteRoot = Path.GetFullPath(root);
        var absoluteItem = Path.GetFullPath(itemPath);
        var relative = Path.GetRelativePath(absoluteRoot, absoluteItem);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative) || !absoluteItem.EndsWith(".strm", StringComparison.OrdinalIgnoreCase)) return null;
        var dir = Path.GetDirectoryName(absoluteItem)!;
        if (string.Equals(Path.GetFileName(dir), ".hdrezka-parts", StringComparison.Ordinal)) dir = Path.GetDirectoryName(dir)!;
        var movieJson = Path.Combine(dir, "hdrezka.json");
        if (File.Exists(movieJson))
        {
            var movie = JsonSerializer.Deserialize<LibraryMovieRecord>(File.ReadAllText(movieJson));
            if (movie is null || CatalogIdentity.FromPath(movie.Path) != movie.Id) return null;
            var partsPath = Path.Combine(dir, "hdrezka-parts.json");
            if (File.Exists(partsPath))
            {
                var parts = JsonSerializer.Deserialize<MultipartMoviePart[]>(File.ReadAllText(partsPath)) ?? [];
                var name = Path.GetFileNameWithoutExtension(absoluteItem);
                var match = Regex.Match(name, @" - pt([0-9]{1,4})$", RegexOptions.CultureInvariant);
                var index = match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n - 1 : 0;
                if (index >= 0 && index < parts.Length)
                {
                    var part = parts[index];
                    return new(movie.Id, movie.Path, part.Season, part.Number, part.Voices, true);
                }
            }
            return new(movie.Id, movie.Path, null, null, movie.Voices);
        }
        var baseName = Path.GetFileNameWithoutExtension(absoluteItem);
        baseName = Regex.Replace(baseName, @" - voice-[0-9]{1,16}$", "", RegexOptions.CultureInvariant);
        var episodeJson = Path.Combine(dir, baseName + ".hdrezka.json");
        if (!File.Exists(episodeJson)) return null;
        var episode = JsonSerializer.Deserialize<LibraryEpisodeRecord>(File.ReadAllText(episodeJson));
        return episode is null || CatalogIdentity.FromPath(episode.Path) != episode.Id ? null : new(episode.Id, episode.Path, episode.Season, episode.Number, episode.Voices, UseMoviePlayer: episode.UseMoviePlayer);
    }
}
