using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class LibraryMediaProvider(IApplicationPaths paths, ILibraryManager library, IMediaEncoder encoder, ILogger<LibraryMediaProvider> logger, IHttpContextAccessor? http = null) : IMediaSourceProvider
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, MediaSourceInfo[] Sources)> Cache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, ManualQualityOption[] Options)> QualityCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, (DateTime Expiry, ManualQualityOption Option)> KnownQualityIds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ShortLivedStreamCache Streams = new(TimeSpan.FromMinutes(2));
    private static readonly ShortLivedProbeCache<MediaInfo> Probes = new(TimeSpan.FromMinutes(2));
    private readonly object _rootLock = new();
    private string? _resolvedRoot;
    private string? _configuredRoot;
    public string Root
    {
        get
        {
            var configured = Plugin.Instance?.Configuration.LibraryRoot;
            lock (_rootLock)
            {
                if (_resolvedRoot is null || !string.Equals(_configuredRoot, configured, StringComparison.Ordinal))
                {
                    _resolvedRoot = LibraryStorage.ResolveRoot(paths.ProgramDataPath, configured,
                        (library.GetVirtualFolders() ?? []).Where(x => x.Name is "Фильмы HDrezka" or "Сериалы HDrezka" or "Мультфильмы HDrezka" or "Аниме HDrezka" or "ТВ-шоу HDrezka").SelectMany(x => x.Locations));
                    _configuredRoot = configured;
                }
                return _resolvedRoot;
            }
        }
    }

    public LibraryVideoRecord? Record(BaseItem item)
    {
        if (string.IsNullOrEmpty(item.Path)) return null;
        try { return LibraryRecordLocator.Load(Root, item.Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            logger.LogWarning("Cannot read HDrezka library record: {ErrorType}", ex.GetType().Name);
            return null;
        }
    }

    public Task<IEnumerable<MediaSourceInfo>> GetMediaSources(BaseItem item, CancellationToken token)
        => GetMediaSourcesForAsync(item, PlaybackSelection.Current, token);

    private static string QualityKey(LibraryVideoRecord record) => $"{record.Id}:{record.Season}:{record.Number}";

    private static string StreamKey(Uri mirror, LibraryVideoRecord record, FreeVoice voice)
        => $"{mirror.GetLeftPart(UriPartial.Authority)}:{record.Path}:{record.Season}:{record.Number}:{voice.Id}";

    private static Task<IReadOnlyList<NamedStream>> GetStreamsAsync(HdrezkaSource source, Uri mirror, LibraryVideoRecord record, FreeVoice voice, CancellationToken token)
        => Streams.GetAsync(StreamKey(mirror, record, voice), ct => record.Season is null || record.Number is null
            ? source.MovieVoiceStreamsFromListingAsync(record.Path, voice, ct)
            : source.EpisodeStreamsFromListingAsync(record.Path, voice, record.Season, record.Number, ct), token);

    public IReadOnlyList<ManualQualityOption> CachedQualityOptions(LibraryVideoRecord record)
        => QualityCache.TryGetValue(QualityKey(record), out var entry) && entry.Expiry > DateTime.UtcNow ? entry.Options : [];

    public ManualQualityOption? FindQualityOption(Guid id)
    {
        var text = id.ToString("N");
        return KnownQualityIds.TryGetValue(text, out var entry) && entry.Expiry > DateTime.UtcNow ? entry.Option : null;
    }

    public async Task PrepareQualityOptionsAsync(Video video, CancellationToken token)
    {
        var record = Record(video);
        if (record is null || CachedQualityOptions(record).Count > 0) return;
        var primary = video.PrimaryVersionId is { } primaryId ? library.GetItemById(primaryId) as Video : video;
        if (primary is null) return;
        var versions = new[] { primary }.Concat(library.GetLinkedAlternateVersions(primary))
            .Concat(library.GetLocalAlternateVersionIds(primary).Select(id => library.GetItemById(id)).OfType<Video>())
            .DistinctBy(v => v.Id).Select(v => new LibraryVersion(v.Id.ToString("N"), v.Path)).ToArray();
        var mapping = LibraryVersionMap.Create(record, versions);
        using var mirror = new MirrorClient(new Uri(Plugin.Instance?.Configuration.MirrorUrl ?? "https://rezka-tv.org/"));
        var source = new HdrezkaSource(mirror);
        var options = new List<ManualQualityOption>();
        foreach (var voice in record.Voices)
        {
            if (!mapping.TryGetValue(voice.Id, out var versionId)) continue;
            var streams = await GetStreamsAsync(source, mirror.CurrentMirror, record, voice, token).ConfigureAwait(false);
            CdnTestController.Remember(streams.FirstOrDefault()?.Stream.Url, mirror.CurrentMirror);
            options.AddRange(ManualQualityOptions.Create(versionId, voice.Title, streams));
        }
        if (options.Count > 0)
        {
            QualityCache[QualityKey(record)] = (DateTime.UtcNow.AddMinutes(10), options.ToArray());
            if (KnownQualityIds.Count > 50000)
                foreach (var entry in KnownQualityIds.Where(x => x.Value.Expiry <= DateTime.UtcNow).Take(5000)) KnownQualityIds.TryRemove(entry.Key, out _);
            foreach (var option in options) KnownQualityIds[option.Id] = (DateTime.UtcNow.AddHours(12), option);
        }
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetMediaSourcesForAsync(BaseItem item, string? selectedSourceId, CancellationToken token)
    {
        var record = Record(item);
        if (record is null || item is not Video video) return [];
        var user = http?.HttpContext?.User.FindFirst("Jellyfin-UserId")?.Value ?? "";
        var device = http?.HttpContext?.User.FindFirst("Jellyfin-DeviceId")?.Value ?? "";
        var continuedVoice = NextEpisodeVoice.Shared.ForNext(user, device, record);
        var preferred = Plugin.Instance?.Configuration.PreferredQuality ?? 0;
        var key = $"{record.Id}:{record.Season}:{record.Number}:{selectedSourceId}:{continuedVoice}:{Plugin.Instance?.Configuration.CdnHost}:{preferred}";
        var gate = Gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(key, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                if (PlaybackSelection.Current is not null)
                    foreach (var entry in cached.Sources)
                        if (record.Voices.FirstOrDefault(v => entry.Name?.StartsWith(v.Title + " — ", StringComparison.Ordinal) == true) is { } cachedVoice)
                            NextEpisodeVoice.Shared.Observe(user, device, record, cachedVoice.Id, entry.RunTimeTicks);
                return cached.Sources;
            }
            var primary = video.PrimaryVersionId is { } primaryId ? library.GetItemById(primaryId) as Video : video;
            if (primary is null) return [];
            var versions = new[] { primary }.Concat(library.GetLinkedAlternateVersions(primary))
                .Concat(library.GetLocalAlternateVersionIds(primary).Select(id => library.GetItemById(id)).OfType<Video>())
                .DistinctBy(v => v.Id).Select(v => new LibraryVersion(v.Id.ToString("N"), v.Path)).ToArray();
            var mapping = LibraryVersionMap.Create(record, versions);
            var requestedVersion = selectedSourceId;
            if (string.IsNullOrEmpty(requestedVersion)) requestedVersion = record.Voices.Select(v => mapping.GetValueOrDefault(v.Id)).FirstOrDefault(id => id is not null);
            var alias = (string?)null;
            if (continuedVoice is not null && string.Equals(requestedVersion, primary.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)
                && mapping.TryGetValue(continuedVoice, out var continuedVersion))
            { alias = requestedVersion; requestedVersion = continuedVersion; }
            using var mirror = new MirrorClient(new Uri(Plugin.Instance?.Configuration.MirrorUrl ?? "https://rezka-tv.org/"));
            var source = new HdrezkaSource(mirror);
            var output = new List<MediaSourceInfo>();
            foreach (var voice in record.Voices)
            {
                if (!mapping.TryGetValue(voice.Id, out var versionId)) continue;
                if (!string.Equals(requestedVersion, versionId, StringComparison.OrdinalIgnoreCase)) continue;
                var streamTimer = System.Diagnostics.Stopwatch.StartNew();
                var candidates = await GetStreamsAsync(source, mirror.CurrentMirror, record, voice, token).ConfigureAwait(false);
                logger.LogInformation("HDrezka stream lookup took {ElapsedMs} ms, {Count} candidates", streamTimer.ElapsedMilliseconds, candidates.Count);
                CdnTestController.Remember(candidates.FirstOrDefault()?.Stream.Url, mirror.CurrentMirror);
                MediaSourceInfo? media = null;
                await MediaSelection.FirstPlayableAsync(candidates, async (candidate, ct) =>
                {
                    var option = new MediaSourceInfo
                    {
                        Id = alias ?? versionId, Name = $"{candidate.Voice} — {candidate.Stream.Quality}p", Path = CdnSelection.Apply(candidate.Stream.Url, Plugin.Instance?.Configuration.CdnHost).AbsoluteUri,
                        Protocol = MediaProtocol.Http, IsRemote = true, SupportsDirectPlay = true, SupportsDirectStream = true, SupportsTranscoding = true,
                        RequiredHttpHeaders = new Dictionary<string, string> { ["Referer"] = mirror.CurrentMirror.AbsoluteUri, ["User-Agent"] = MirrorClient.Agent }
                    };
                    try
                    {
                        var probe = await Probes.GetAsync(option.Path + "\n" + mirror.CurrentMirror.AbsoluteUri, async probeToken =>
                        {
                            var probeTimer = System.Diagnostics.Stopwatch.StartNew();
                            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(probeToken);
                            timeout.CancelAfter(TimeSpan.FromSeconds(8));
                            var result = await encoder.GetMediaInfo(new MediaInfoRequest { MediaSource = option, MediaType = DlnaProfileType.Video, ExtractChapters = false }, timeout.Token).ConfigureAwait(false);
                            logger.LogInformation("HDrezka {Quality}p stream probe took {ElapsedMs} ms", candidate.Stream.Quality, probeTimer.ElapsedMilliseconds);
                            return result;
                        }, value => MediaInfoMerger.Apply(option, value), ct).ConfigureAwait(false);
                        if (!MediaInfoMerger.Apply(option, probe)) return false;
                        foreach (var target in new[] { video, primary }.DistinctBy(x => x.Id))
                        {
                            if (!PlaybackRuntime.TryFill(target, option.RunTimeTicks)) continue;
                            try { await library.UpdateItemAsync(target, target.GetParent(), ItemUpdateType.MetadataEdit, ct).ConfigureAwait(false); }
                            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                            { logger.LogWarning("Could not save HDrezka playback duration: {ErrorType}", ex.GetType().Name); }
                        }
                        if (PlaybackSelection.Current is not null) NextEpisodeVoice.Shared.Observe(user, device, record, voice.Id, option.RunTimeTicks);
                        logger.LogInformation("HDrezka selected voice {Voice}; automatic episode continuation: {Continuation}", voice.Title, alias is not null);
                        media = option;
                        return true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    { logger.LogWarning("Cannot probe HDrezka stream at {Quality}p: {ErrorType}", candidate.Stream.Quality, ex.GetType().Name); return false; }
                }, token, preferred).ConfigureAwait(false);
                if (media is not null) output.Add(media);
            }
            var result = output.ToArray();
            if (result.Length > 0) Cache[key] = (DateTime.UtcNow.AddMinutes(3), result);
            return result;
        }
        finally { gate.Release(); }
    }

    public Task<ILiveStream> OpenMediaSource(string openToken, List<ILiveStream> currentLiveStreams, CancellationToken cancellationToken) => throw new NotSupportedException();
}

