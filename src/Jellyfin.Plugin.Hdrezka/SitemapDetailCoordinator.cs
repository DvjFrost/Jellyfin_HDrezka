using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class SitemapDetailCoordinator(LibraryMediaProvider media, ILibraryManager library, SeriesMigration migration, ILogger<SitemapDetailCoordinator> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _fresh = new(StringComparer.Ordinal);

    public async Task<Guid?> PrepareAsync(BaseItem item, CancellationToken token)
    {
        if (string.IsNullOrEmpty(item.Path)) return null;
        var root = Path.GetFullPath(media.Root);
        var relative = Path.GetRelativePath(root, Path.GetFullPath(item.Path));
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)) return null;
        var section = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        var kind = section switch { "films" => CatalogKind.Movie, "series" => CatalogKind.Series, "cartoons" => CatalogKind.Cartoon, "animation" => CatalogKind.Anime, "show" => CatalogKind.Show, _ => CatalogKind.Other };
        if (kind == CatalogKind.Other) return null;
        string? id;
        string? path;
        var series = item is Series;
        if (series)
        {
            var record = LibrarySeriesLocator.Load(root, item.Path);
            id = record?.Id;
            path = record?.Path;
        }
        else
        {
            if (item is not Video) return null;
            var record = media.Record(item);
            if (record?.Season is not null) return null;
            id = record?.Id;
            path = record?.Path;
        }
        if (id is null || path is null) return null;
        if (_fresh.TryGetValue(id, out var until) && until > DateTime.UtcNow) return SeriesMigration.FindReplacement(root, item.Id);
        var gate = _gates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_fresh.TryGetValue(id, out until) && until > DateTime.UtcNow) return SeriesMigration.FindReplacement(root, item.Id);
            var mirrorUri = new Uri(Plugin.Instance?.Configuration.MirrorUrl ?? "https://rezka-tv.org/");
            using var mirror = new MirrorClient(mirrorUri);
            var video = new SitemapVideo(id, "", null, kind, new Uri(mirror.CurrentMirror, path));
            var match = new TmdbMatch(id, TmdbMatchStatus.Uncertain, null, null, null, series ? "tv" : "movie");
            var result = await SitemapDetailHydrator.HydrateAsync(root, video, match, new HdrezkaSource(mirror), token).ConfigureAwait(false);
            if (result.StagedSeriesDirectory is not null && item is Video oldMovie)
            {
                var replacement = await migration.ConvertAsync(root, oldMovie, result.StagedSeriesDirectory, id, token).ConfigureAwait(false);
                _fresh[id] = DateTime.UtcNow.AddMinutes(10);
                return replacement;
            }
            var nfoPath = Path.Combine(root, section, LibraryPath.TitleDirectory(id), series ? "tvshow.nfo" : "movie.nfo");
            // Deliberately limited to explicitly listed test items during the pilot.
            // Without the environment variable this code never contacts TMDB.
            var pilotIds = (Environment.GetEnvironmentVariable("HDREZKA_TMDB_PILOT_IDS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var tmdbToken = Plugin.Instance?.Configuration.TmdbReadAccessToken;
            if (pilotIds.Contains(id, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(tmdbToken))
            {
                try
                {
                    var nfo = System.Xml.Linq.XDocument.Load(nfoPath);
                    var tmdbIdText = nfo.Root?.Element("tmdbid")?.Value;
                    var marker = Path.Combine(Path.GetDirectoryName(nfoPath)!, "hdrezka-tmdb-details.txt");
                    if (int.TryParse(tmdbIdText, out var tmdbId) && tmdbId > 0 && (!File.Exists(marker) || (await File.ReadAllTextAsync(marker, token).ConfigureAwait(false)).Trim() != tmdbIdText))
                    {
                        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
                        await TmdbMetadataWriter.EnrichAsync(nfoPath, tmdbId, series, new TmdbClient(http, tmdbToken), token).ConfigureAwait(false);
                        await MovieLibraryWriter.AtomicWriteAsync(marker, tmdbIdText!, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogWarning("Could not enrich pilot item from TMDB: {ErrorType}", ex.GetType().Name); }
            }
            var hydratedName = System.Xml.Linq.XDocument.Load(nfoPath).Root?.Element("title")?.Value;
            if (item is Folder folder)
            {
                var options = new MediaBrowser.Controller.Providers.MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(BaseItem.FileSystem))
                { MetadataRefreshMode = MediaBrowser.Controller.Providers.MetadataRefreshMode.Default, EnableRemoteContentProbe = false, ForceSave = true };
                await folder.RefreshMetadata(options, token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(hydratedName) && folder.Name != hydratedName)
                {
                    folder.Name = hydratedName;
                    await library.UpdateItemAsync(folder, folder.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
                }
                await folder.ValidateChildren(new Progress<double>(), token).ConfigureAwait(false);
                await LibraryVersionTitles.RepairAsync(folder, library, media, token).ConfigureAwait(false);
            }
            else if (item.GetParent() is Folder parent)
            {
                if (item is Video videoItem)
                {
                    var primary = videoItem.PrimaryVersionId is { } primaryId ? library.GetItemById(primaryId) as Video : videoItem;
                    var updated = primary is null ? null : media.Record(primary);
                    if (primary is not null && updated is not null && !string.IsNullOrEmpty(primary.Path))
                    {
                        var existing = library.GetLocalAlternateVersionIds(primary).Select(library.GetItemById).OfType<Video>().Select(v => v.Path).Where(p => !string.IsNullOrEmpty(p));
                        primary.LocalAlternateVersions = MovieVersionPaths.Collect(Path.GetDirectoryName(primary.Path)!, updated.Id, updated.Voices, existing!).ToArray();
                        var options = new MediaBrowser.Controller.Providers.MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(BaseItem.FileSystem))
                        { MetadataRefreshMode = MediaBrowser.Controller.Providers.MetadataRefreshMode.Default, EnableRemoteContentProbe = false, ForceSave = true };
                        await primary.RefreshMetadata(options, token).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(hydratedName) && primary.Name != hydratedName)
                        {
                            primary.Name = hydratedName;
                            await library.UpdateItemAsync(primary, primary.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
                        }
                        await LibraryVersionTitles.RepairVideoAsync(primary, library, media, token).ConfigureAwait(false);
                    }
                }
            }
            _fresh[id] = DateTime.UtcNow.AddMinutes(10);
        }
        catch (MirrorAccessException ex)
        {
            logger.LogWarning("Could not load HDrezka item details: mirror HTTP {Status}", ex.StatusCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Could not load HDrezka item details: {ErrorType}", ex.GetType().Name);
        }
        finally { gate.Release(); }
        return null;
    }
}
