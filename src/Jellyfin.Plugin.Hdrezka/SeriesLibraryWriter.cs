using System.Text.Json;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record SeriesVoiceEpisodes(FreeVoice Voice, IReadOnlyList<Episode> Episodes);
public sealed record LibraryEpisodeRecord(string Id, string Path, string Season, string Number, IReadOnlyList<FreeVoice> Voices, bool UseMoviePlayer = false);

public static class SeriesLibraryWriter
{
    public static async Task WriteAsync(string root, CatalogEntry entry, CatalogDetail detail, IReadOnlyList<SeriesVoiceEpisodes> available, CancellationToken token, bool useMoviePlayer = false)
    {
        var id = CatalogIdentity.FromPath(entry.Url.AbsolutePath) ?? throw new ArgumentException("Invalid video identity.", nameof(entry));
        if (!detail.HasEpisodes) throw new ArgumentException("A movie cannot be written as a series.", nameof(detail));
        var dir = Path.Combine(Path.GetFullPath(root), LibraryPath.TitleDirectory(id));
        Directory.CreateDirectory(dir);
        var nfoPath = Path.Combine(dir, "tvshow.nfo");
        await MetadataFileLock.RunAsync(nfoPath, async () =>
        {
            var nfo = MovieLibraryWriter.MergeNfo(nfoPath, "tvshow", entry, detail);
            await MovieLibraryWriter.AtomicWriteAsync(nfoPath, nfo.ToString(), token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        var episodes = available.SelectMany(v => v.Episodes.Select(e => (v.Voice, Episode: e)))
            .GroupBy(x => (x.Episode.Season, x.Episode.Number));
        foreach (var group in episodes)
        {
            token.ThrowIfCancellationRequested();
            if (!int.TryParse(group.Key.Season, out var season) || season is < 0 or > 9999 ||
                !int.TryParse(group.Key.Number, out var number) || number is < 1 or > 9999) continue;
            var seasonDir = Path.Combine(dir, $"Season {season:D2}");
            Directory.CreateDirectory(seasonDir);
            var baseName = $"{LibraryPath.TitleDirectory(id)} S{season:D2}E{number:D2}";
            var versions = group.Select(x => x.Voice).DistinctBy(v => v.Id).ToArray();
            var record = new LibraryEpisodeRecord(id, entry.Url.AbsolutePath, group.Key.Season, group.Key.Number, versions, useMoviePlayer);
            await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(seasonDir, baseName + ".hdrezka.json"), JsonSerializer.Serialize(record), token).ConfigureAwait(false);
            var episodeNfoPath = Path.Combine(seasonDir, baseName + ".nfo");
            XDocument? episodeNfo = null;
            var writeNfo = true;
            if (File.Exists(episodeNfoPath))
            {
                try { episodeNfo = XDocument.Load(episodeNfoPath); }
                catch (System.Xml.XmlException) { writeNfo = false; }
                if (episodeNfo?.Root?.Name != "episodedetails") writeNfo = false;
            }
            if (writeNfo)
            {
                var existingTitle = episodeNfo?.Root?.Element("title")?.Value;
                var selectedTitle = EpisodeTitleSelection.Choose(existingTitle, group.First().Episode.Title, number);
                episodeNfo ??= new XDocument(new XElement("episodedetails", new XElement("season", season), new XElement("episode", number)));
                var titleElement = episodeNfo.Root!.Element("title");
                if (titleElement is null) episodeNfo.Root.AddFirst(new XElement("title", selectedTitle));
                else titleElement.Value = selectedTitle;
                await MovieLibraryWriter.AtomicWriteAsync(episodeNfoPath, episodeNfo.ToString(), token).ConfigureAwait(false);
            }
            var primary = Path.Combine(seasonDir, baseName + ".strm");
            if (!File.Exists(primary)) await MovieLibraryWriter.AtomicWriteAsync(primary, MovieLibraryWriter.Placeholder, token).ConfigureAwait(false);
            foreach (var voice in versions.Skip(1))
            {
                var safeVersion = LibraryPath.VersionFileName(id, voice.Id);
                var suffix = safeVersion[(LibraryPath.TitleDirectory(id).Length)..];
                var path = Path.Combine(seasonDir, baseName + suffix);
                if (!File.Exists(path)) await MovieLibraryWriter.AtomicWriteAsync(path, MovieLibraryWriter.Placeholder, token).ConfigureAwait(false);
            }
        }
    }
}
