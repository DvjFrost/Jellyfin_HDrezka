using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record SitemapHydrationResult(string? StagedSeriesDirectory);

public static class SitemapDetailHydrator
{
    public static async Task<SitemapHydrationResult> HydrateAsync(string root, SitemapVideo video, TmdbMatch? match, HdrezkaSource source, CancellationToken token, string? preservedTitle = null)
    {
        var section = video.Kind switch { CatalogKind.Movie => "films", CatalogKind.Series => "series", CatalogKind.Anime => "animation", CatalogKind.Show => "show", _ => "cartoons" };
        var library = Path.Combine(root, section);
        var dir = Path.Combine(library, LibraryPath.TitleDirectory(video.Id));
        var isSeries = video.Kind == CatalogKind.Series || (video.Kind is CatalogKind.Cartoon or CatalogKind.Anime or CatalogKind.Show && match?.MediaType == "tv");
        var nfoPath = Path.Combine(dir, isSeries ? "tvshow.nfo" : "movie.nfo");
        if (!File.Exists(nfoPath)) throw new InvalidOperationException("Catalog item must be indexed first.");
        var nfo = XDocument.Load(nfoPath);
        var title = nfo.Root?.Element("title")?.Value;
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidDataException("Catalog item has no title.");
        var thumb = nfo.Root?.Element("thumb")?.Value;
        var poster = Uri.TryCreate(thumb, UriKind.Absolute, out var image) && image.Scheme == Uri.UriSchemeHttps ? image : null;
        var listing = await source.DetailAndVoicesAsync(video.Url.AbsolutePath, token).ConfigureAwait(false);
        var state = SitemapMetadataStore.Read(dir, video.Id);
        var fromTmdb = state?.FromTmdb ?? match?.Status == TmdbMatchStatus.Matched;
        if (!fromTmdb)
        {
            title = listing.Detail.Title;
            poster = listing.Detail.Poster ?? poster;
        }
        else poster ??= listing.Detail.Poster;
        if (string.IsNullOrWhiteSpace(preservedTitle) && CatalogTitleRepair.IsSitePlaceholder(title, video.Url.AbsolutePath))
            title = listing.Detail.Title;
        if (!string.IsNullOrWhiteSpace(preservedTitle)) title = preservedTitle;
        if (video.Kind == CatalogKind.Movie && listing.Detail.HasEpisodes)
        {
            var available = new List<SeriesVoiceEpisodes>();
            foreach (var voice in listing.Voices)
                available.Add(new SeriesVoiceEpisodes(voice, await source.EpisodesFromListingAsync(video.Url.AbsolutePath, listing, voice, token).ConfigureAwait(false)));
            var year = nfo.Root?.Element("year")?.Value ?? "";
            var film = new CatalogEntry(title, video.Url, poster, video.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? year, video.Kind);
            await MultipartMovieWriter.WriteAsync(library, film, listing.Detail with { Title = title }, available, token).ConfigureAwait(false);
            await SitemapMetadataStore.WriteAsync(dir, new SitemapMetadata(video.Id, fromTmdb, !fromTmdb), token).ConfigureAwait(false);
            return new SitemapHydrationResult(null);
        }
        var transition = !isSeries && listing.Detail.HasEpisodes && video.Kind is CatalogKind.Cartoon or CatalogKind.Anime or CatalogKind.Show;
        if (!isSeries && listing.Detail.HasEpisodes && !transition) throw new InvalidOperationException("A film was identified as a series.");
        if (transition) isSeries = true;
        var savedYear = nfo.Root?.Element("year")?.Value ?? "";
        var entry = new CatalogEntry(title, video.Url, poster, video.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? savedYear, video.Kind);
        var detail = listing.Detail with { Title = title, HasEpisodes = isSeries };
        if (isSeries)
        {
            var episodes = new List<SeriesVoiceEpisodes>();
            foreach (var voice in listing.Voices)
                episodes.Add(new SeriesVoiceEpisodes(voice, listing.Detail.HasEpisodes
                    ? await source.EpisodesFromListingAsync(video.Url.AbsolutePath, listing, voice, token).ConfigureAwait(false)
                    : [new Episode("1", "1", "Полная версия")]));
            var destination = transition ? Path.Combine(root, ".hdrezka-staging", video.Id) : library;
            await SeriesLibraryWriter.WriteAsync(destination, entry, detail, episodes, token, !listing.Detail.HasEpisodes).ConfigureAwait(false);
            if (transition)
            {
                var staged = Path.Combine(destination, LibraryPath.TitleDirectory(video.Id));
                await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(staged, "hdrezka-series.json"), System.Text.Json.JsonSerializer.Serialize(new LibrarySeriesRecord(video.Id, video.Url.AbsolutePath)), token).ConfigureAwait(false);
                await SitemapMetadataStore.WriteAsync(staged, new SitemapMetadata(video.Id, fromTmdb, !fromTmdb), token).ConfigureAwait(false);
                return new SitemapHydrationResult(staged);
            }
        }
        else await MovieLibraryWriter.WriteAsync(library, entry, detail, listing.Voices, token).ConfigureAwait(false);
        await SitemapMetadataStore.WriteAsync(dir, new SitemapMetadata(video.Id, fromTmdb, !fromTmdb), token).ConfigureAwait(false);
        return new SitemapHydrationResult(null);
    }
}
