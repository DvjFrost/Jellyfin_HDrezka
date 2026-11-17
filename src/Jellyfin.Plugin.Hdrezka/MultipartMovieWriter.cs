using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record MultipartMoviePart(string Season, string Number, string Title, IReadOnlyList<FreeVoice> Voices);

public static class MultipartMovieWriter
{
    public static IReadOnlyList<FreeVoice> CommonVoices(IReadOnlyList<MultipartMoviePart> parts)
        => parts.Count == 0 ? [] : parts[0].Voices.Where(voice => parts.Skip(1).All(part => part.Voices.Any(other => other.Id == voice.Id))).ToArray();

    public static async Task WriteAsync(string library, CatalogEntry entry, CatalogDetail detail, IReadOnlyList<SeriesVoiceEpisodes> available, CancellationToken token)
    {
        var id = CatalogIdentity.FromPath(entry.Url.AbsolutePath) ?? throw new ArgumentException("Invalid video identity.", nameof(entry));
        var parts = available.SelectMany(x => x.Episodes.Select(e => (x.Voice, Episode: e)))
            .Where(x => int.TryParse(x.Episode.Season, out _) && int.TryParse(x.Episode.Number, out _))
            .GroupBy(x => (x.Episode.Season, x.Episode.Number))
            .OrderBy(x => int.Parse(x.Key.Season, System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(x => int.Parse(x.Key.Number, System.Globalization.CultureInfo.InvariantCulture))
            .Select(g => new MultipartMoviePart(g.Key.Season, g.Key.Number, g.First().Episode.Title, g.Select(x => x.Voice).DistinctBy(x => x.Id).ToArray()))
            .ToArray();
        await MovieLibraryWriter.WriteAsync(library, entry, detail with { HasEpisodes = false }, parts.FirstOrDefault()?.Voices ?? [], token).ConfigureAwait(false);
        var dir = Path.Combine(library, LibraryPath.TitleDirectory(id));
        var partDir = Path.Combine(dir, ".hdrezka-parts");
        if (parts.Length > 1) Directory.CreateDirectory(partDir);
        for (var i = 1; i < parts.Length; i++)
        {
            var path = Path.Combine(partDir, LibraryPath.TitleDirectory(id) + " - pt" + (i + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".strm");
            if (!File.Exists(path)) await MovieLibraryWriter.AtomicWriteAsync(path, MovieLibraryWriter.Placeholder, token).ConfigureAwait(false);
        }
        await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(dir, "hdrezka-parts.json"), JsonSerializer.Serialize(parts), token).ConfigureAwait(false);
    }
}
