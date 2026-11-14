using System.Text.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class TmdbEnrichmentTask(IApplicationPaths paths, LibraryMediaProvider media, ILibraryManager library, ITaskManager tasks, ILogger<TmdbEnrichmentTask> logger) : IScheduledTask
{
    public string Name => "Обновить данные HDrezka из TMDB";
    public string Description => "После создания каталога дополнить названия и постеры из TMDB.";
    public string Category => "HDrezka";
    public string Key => "HdrezkaTmdbEnrichment";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
    public static bool IsCatalogReady(string directory) => File.Exists(Path.Combine(directory, "sitemap.json")) && File.Exists(Path.Combine(directory, "catalog-ready"));

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        while (tasks.ScheduledTasks.Any(x => x.ScheduledTask is SitemapIndexTask && x.State != TaskState.Idle))
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var token = Plugin.Instance?.Configuration.TmdbReadAccessToken;
        if (string.IsNullOrWhiteSpace(token)) { progress.Report(100); return; }
        var indexDir = Path.Combine(paths.DataPath, "hdrezka-catalog");
        if (!IsCatalogReady(indexDir))
        {
            tasks.QueueIfNotRunning<SitemapIndexTask>();
            logger.LogInformation("TMDB enrichment is waiting for the initial HDrezka catalog");
            progress.Report(100);
            return;
        }
        var videos = (JsonSerializer.Deserialize<List<SitemapVideo>>(await File.ReadAllTextAsync(Path.Combine(indexDir, "sitemap.json"), cancellationToken).ConfigureAwait(false)) ?? []).Where(x => x.Kind is CatalogKind.Movie or CatalogKind.Series).ToList();
        var store = new TmdbMatchStore(Path.Combine(indexDir, "tmdb-matches.jsonl"));
        var cached = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var matcher = new TmdbMatcher(new TmdbClient(http, token), TimeSpan.FromMilliseconds(200));
        var update = new Progress<TmdbIndexSummary>(s => { if (s.Total > 0) progress.Report((double)s.Processed * 100 / s.Total); });
        var root = media.Root;
        var summary = await new TmdbIndex(matcher, store).RunAsync(videos, cancellationToken, update, async (video, match, ct) =>
        {
            if (match.Status != TmdbMatchStatus.Matched) return;
            var section = video.Kind switch { CatalogKind.Movie => "films", CatalogKind.Series => "series", CatalogKind.Anime => "animation", CatalogKind.Show => "show", _ => "cartoons" };
            var dir = Path.Combine(root, section, LibraryPath.TitleDirectory(video.Id));
            var upToDate = cached.TryGetValue(video.Id, out var old) && TmdbMatcher.IsCurrent(video, old) && SitemapMetadataStore.Read(dir, video.Id)?.FromTmdb == true;
            if (!upToDate) await SitemapLibraryWriter.WriteAsync(root, video, match, ct).ConfigureAwait(false);
            var isSeries = File.Exists(Path.Combine(dir, "tvshow.nfo"));
            var itemPath = isSeries ? dir : Path.Combine(dir, LibraryPath.PrimaryFileName(video.Id));
            var type = isSeries ? typeof(Series) : typeof(Movie);
            if (library.GetItemById(library.GetNewItemId(itemPath, type)) is { } item)
            {
                if (!upToDate)
                {
                    var options = new MetadataRefreshOptions(new DirectoryService(BaseItem.FileSystem)) { MetadataRefreshMode = MetadataRefreshMode.FullRefresh, EnableRemoteContentProbe = false, ForceSave = true };
                    await item.RefreshMetadata(options, ct).ConfigureAwait(false);
                }
                if (CatalogTitleRepair.ShouldReplace(item.Name, video.Query, match.Title, item.IsLocked || item.LockedFields.Contains(MetadataField.Name)))
                {
                    item.Name = match.Title!;
                    await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false);
                }
            }
        }).ConfigureAwait(false);
        logger.LogInformation("HDrezka TMDB index: {Total} total, {Matched} matched, {Uncertain} uncertain, {NoResults} no search results", summary.Total, summary.Matched, summary.Uncertain, summary.NoResults);
        progress.Report(100);
    }
}
