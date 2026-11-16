using System.Text.Json;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class SitemapIndexTask(IApplicationPaths paths, LibraryMediaProvider media, ILibraryManager library, ITaskManager tasks, ILogger<SitemapIndexTask> logger) : IScheduledTask
{
    public string Name => "Индекс HDrezka и TMDB";
    public string Description => "Загрузить каталог из sitemap и при наличии ключа дополнить данные из TMDB.";
    public string Category => "HDrezka";
    public string Key => "HdrezkaSitemapTmdbIndex";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HDREZKA_TMDB_PILOT_IDS"))
        ? [new() { Type = TaskTriggerInfoType.StartupTrigger }, new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(1).Ticks }]
        : [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("HDrezka configuration is unavailable.");
        var folders = await SitemapLibraries.EnsureAsync(library, media.Root, cancellationToken).ConfigureAwait(false);
        using var mirror = new MirrorClient(new Uri(config.MirrorUrl), requestInterval: TimeSpan.FromSeconds(1));
        var videos = await SitemapLoader.LoadAsync(mirror, cancellationToken).ConfigureAwait(false);
        var indexDir = Path.Combine(paths.DataPath, "hdrezka-catalog");
        Directory.CreateDirectory(indexDir);
        var sitemapPath = Path.Combine(indexDir, "sitemap.json");
        var previous = new Dictionary<string, SitemapVideo>(StringComparer.Ordinal);
        if (File.Exists(sitemapPath))
        {
            try
            {
                var savedVideos = JsonSerializer.Deserialize<List<SitemapVideo>>(await File.ReadAllTextAsync(sitemapPath, cancellationToken).ConfigureAwait(false)) ?? [];
                foreach (var saved in savedVideos) previous.TryAdd(saved.Id, saved);
            }
            catch (JsonException ex) { logger.LogWarning(ex, "Saved HDrezka sitemap is invalid; rebuilding its index"); }
        }
        var store = new TmdbMatchStore(Path.Combine(indexDir, "tmdb-matches.jsonl"));
        var cached = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var prepared = 0;
        var changed = 0;
        foreach (var video in videos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = cached.TryGetValue(video.Id, out var saved) && TmdbMatcher.IsCurrent(video, saved) ? saved : null;
            var section = video.Kind == CatalogKind.Movie ? "films" : "series";
            var dir = Path.Combine(media.Root, section, LibraryPath.TitleDirectory(video.Id));
            var exists = File.Exists(Path.Combine(dir, video.Kind == CatalogKind.Movie ? "movie.nfo" : "tvshow.nfo"));
            if (SitemapChanges.NeedsWrite(video, previous, exists))
            {
                await SitemapLibraryWriter.WriteAsync(media.Root, video, match, cancellationToken).ConfigureAwait(false);
                changed++;
                if (changed == 100 || changed % 5000 == 0) await SitemapLibraries.ScanAsync(library, folders, cancellationToken).ConfigureAwait(false);
            }
            prepared++;
            if (prepared % 100 == 0) progress.Report(100.0 * prepared / videos.Count);
        }
        if (changed > 0) await SitemapLibraries.ScanAsync(library, folders, cancellationToken).ConfigureAwait(false);
        await MovieLibraryWriter.AtomicWriteAsync(sitemapPath, JsonSerializer.Serialize(videos), cancellationToken).ConfigureAwait(false);
        await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(indexDir, "catalog-ready"), "ready", cancellationToken).ConfigureAwait(false);
        logger.LogInformation("HDrezka catalog prepared: {Total} entries, {Changed} new or changed", videos.Count, changed);
        if (!string.IsNullOrWhiteSpace(Plugin.Instance?.Configuration.TmdbReadAccessToken)) tasks.QueueScheduledTask<TmdbEnrichmentTask>();
        progress.Report(100);
    }
}
