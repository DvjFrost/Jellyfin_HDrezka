using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record LibrarySeriesRecord(string Id, string Path);

public static class SitemapLibraryWriter
{
    public static async Task WriteAsync(string root, SitemapVideo video, TmdbMatch? match, CancellationToken token, string? preservedTitle = null)
    {
        if (match is not null && match.Id != video.Id) throw new ArgumentException("TMDB result belongs to another video.", nameof(match));
        var matched = match?.Status == TmdbMatchStatus.Matched && !string.IsNullOrWhiteSpace(match.Title);
        var title = !string.IsNullOrWhiteSpace(preservedTitle) ? preservedTitle : matched ? match!.Title : video.Query;
        Uri? poster = null;
        if (match?.Status == TmdbMatchStatus.Matched && match.PosterPath is { } p && Regex.IsMatch(p, @"^/[a-zA-Z0-9_-]+\.(jpg|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            poster = new Uri("https://image.tmdb.org/t/p/w500" + p);
        var description = video.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var entry = new CatalogEntry(title!, video.Url, poster, description, video.Kind);
        var section = video.Kind switch { CatalogKind.Movie => "films", CatalogKind.Series => "series", CatalogKind.Anime => "animation", CatalogKind.Show => "show", _ => "cartoons" };
        var library = Path.Combine(root, section);
        var dir = Path.Combine(library, LibraryPath.TitleDirectory(video.Id));
        var isSeries = video.Kind == CatalogKind.Series || (video.Kind is CatalogKind.Cartoon or CatalogKind.Anime or CatalogKind.Show && match?.MediaType == "tv");
        // An existing library item cannot change Jellyfin type without a controlled migration.
        // Keep its current type so a later TMDB match cannot create a second conflicting item.
        if (video.Kind is CatalogKind.Cartoon or CatalogKind.Anime or CatalogKind.Show)
        {
            if (File.Exists(Path.Combine(dir, "tvshow.nfo"))) isSeries = true;
            else if (File.Exists(Path.Combine(dir, "movie.nfo"))) isSeries = false;
        }
        var nfo = Path.Combine(dir, isSeries ? "tvshow.nfo" : "movie.nfo");
        var previous = SitemapMetadataStore.Read(dir, video.Id);
        var keepSiteMetadata = !matched && previous?.SiteMetadataLoaded == true;
        var keepTmdbMetadata = !matched && previous?.FromTmdb == true;
        var keepExistingMetadata = !matched && previous is null && File.Exists(nfo);
        string overview = "";
        if (File.Exists(nfo))
        {
            try
            {
                var saved = XDocument.Load(nfo).Root;
                overview = saved?.Element("plot")?.Value ?? "";
                if (keepSiteMetadata || keepTmdbMetadata || keepExistingMetadata)
                {
                    title = saved?.Element("title")?.Value ?? title;
                    var thumb = saved?.Element("thumb")?.Value;
                    if (Uri.TryCreate(thumb, UriKind.Absolute, out var previousPoster) && previousPoster.Scheme == Uri.UriSchemeHttps) poster = previousPoster;
                    entry = entry with { Title = title!, Poster = poster };
                }
            }
            catch (System.Xml.XmlException) { }
        }
        if (isSeries)
        {
            await SeriesLibraryWriter.WriteAsync(library, entry, new CatalogDetail(title!, overview, true), [], token).ConfigureAwait(false);
            await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(dir, "hdrezka-series.json"), JsonSerializer.Serialize(new LibrarySeriesRecord(video.Id, video.Url.AbsolutePath)), token).ConfigureAwait(false);
        }
        else
        {
            var primary = Path.Combine(dir, LibraryPath.PrimaryFileName(video.Id));
            var existing = File.Exists(primary) ? LibraryRecordLocator.Load(root, primary) : null;
            await MovieLibraryWriter.WriteAsync(library, entry, new CatalogDetail(title!, overview, false), existing?.Voices ?? [], token).ConfigureAwait(false);
        }
        if (matched && match?.TmdbId is > 0)
        {
            await MetadataFileLock.RunAsync(nfo, async () =>
            {
                var document = XDocument.Load(nfo);
                var existing = document.Root?.Element("tmdbid");
                if (existing is null) document.Root?.Add(new XElement("tmdbid", match.TmdbId.Value));
                // Existing identifiers can be user-corrected in Jellyfin and must not be overwritten.
                await MovieLibraryWriter.AtomicWriteAsync(nfo, document.ToString(), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }
        await SitemapMetadataStore.WriteAsync(dir, new SitemapMetadata(video.Id, matched || keepTmdbMetadata, keepSiteMetadata || keepExistingMetadata), token).ConfigureAwait(false);
    }
}
