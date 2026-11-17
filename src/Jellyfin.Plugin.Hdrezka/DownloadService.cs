using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record DownloadProgress(long Bytes, long? TotalBytes);

public sealed class DownloadService(IApplicationPaths paths, ILibraryManager library, LibraryMediaProvider media, IMediaEncoder encoder, ILogger<DownloadService> logger) : BackgroundService
{
    private readonly Task<DownloadQueue> _queue = DownloadQueue.OpenAsync(new DownloadQueueStore(Path.Combine(paths.DataPath, "hdrezka-downloads")), CancellationToken.None);
    private readonly HttpClient _cdnClient = new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private volatile bool _stoppedByMirror;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _active = new();
    private readonly ConcurrentDictionary<Guid, DownloadProgress> _progress = new();
    private MirrorClient? _mirror;
    private string? _mirrorUrl;
    private HdrezkaSource? _source;
    private DateTime _sourceExpiry;

    public async Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken token) => await (await _queue.ConfigureAwait(false)).ListAsync(token).ConfigureAwait(false);
    public async Task AddAsync(DownloadJob job, CancellationToken token) => await (await _queue.ConfigureAwait(false)).AddAsync(job, token).ConfigureAwait(false);
    public async Task AddRangeAsync(IReadOnlyList<DownloadJob> jobs, CancellationToken token) => await (await _queue.ConfigureAwait(false)).AddRangeAsync(jobs, token).ConfigureAwait(false);
    public async Task<bool> RemoveAsync(Guid id, CancellationToken token) => await (await _queue.ConfigureAwait(false)).RemoveAsync(id, token).ConfigureAwait(false);
    public async Task<int> ClearFinishedAsync(CancellationToken token) => await (await _queue.ConfigureAwait(false)).ClearFinishedAsync(token).ConfigureAwait(false);
    public bool StoppedByMirror => _stoppedByMirror;
    public IReadOnlyDictionary<string, DownloadProgress> Progress => _progress.ToDictionary(pair => pair.Key.ToString("N"), pair => pair.Value);

    public async Task<bool> CancelAsync(Guid id, CancellationToken token)
    {
        if (!await (await _queue.ConfigureAwait(false)).CancelAsync(id, token).ConfigureAwait(false)) return false;
        if (_active.TryGetValue(id, out var cancellation))
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var queue = await _queue.ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_stoppedByMirror) { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false); continue; }
                var worked = await queue.ProcessNextAsync(ExecuteJobAsync, stoppingToken).ConfigureAwait(false);
                if (!worked) await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("Очередь загрузок HDrezka остановлена: {ErrorType}", ex.GetType().Name); }
    }

    private async Task ExecuteJobAsync(DownloadJob job, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _active[job.Id] = cancellation;
        try
        {
        if ((await (await _queue.ConfigureAwait(false)).ListAsync(token).ConfigureAwait(false)).FirstOrDefault(item => item.Id == job.Id)?.Status == DownloadJobStatus.Cancelled) return;
        token = cancellation.Token;
        var root = DownloadLibraries.Resolve(library.GetVirtualFolders(), media.Root, job.LibraryId, job.Season is not null && !job.MoviePart)
            ?? throw new InvalidOperationException("Папка медиатеки больше недоступна.");
        var config = Plugin.Instance?.Configuration ?? throw new InvalidOperationException("Настройки плагина недоступны.");
        if (_mirror is null || !string.Equals(_mirrorUrl, config.MirrorUrl, StringComparison.OrdinalIgnoreCase))
        {
            _mirror?.Dispose();
            _mirror = new MirrorClient(new Uri(config.MirrorUrl));
            _mirrorUrl = config.MirrorUrl;
            _source = null;
        }
        if (_source is null || _sourceExpiry <= DateTime.UtcNow)
        {
            _source = new HdrezkaSource(_mirror);
            _sourceExpiry = DateTime.UtcNow.AddMinutes(10);
        }
        var mirror = _mirror;
        try
        {
            var quality = await DownloadProcessor.ExecuteAsync(job, root, mirror, _cdnClient, config.CdnHost, token, message => logger.LogInformation("Загрузка HDrezka: {Diagnostic}", message), (bytes, total) => _progress[job.Id] = new DownloadProgress(bytes, total), _source).ConfigureAwait(false);
            var destinationPath = DownloadPaths.Resolve(root, job.RelativePath);
            int? width = null, height = null;
            try
            {
                using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var info = await encoder.GetMediaInfo(new MediaInfoRequest { MediaSource = new MediaSourceInfo { Path = destinationPath, Protocol = MediaProtocol.File }, MediaType = DlnaProfileType.Video, ExtractChapters = false }, probeTimeout.Token).ConfigureAwait(false);
                var video = info?.MediaStreams?.FirstOrDefault(stream => stream.Type == MediaStreamType.Video);
                width = video?.Width; height = video?.Height;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            { logger.LogWarning("Не удалось определить разрешение загруженного видео: {ErrorType}", ex.GetType().Name); }
            await (await _queue.ConfigureAwait(false)).SetActualQualityAsync(job.Id, quality, CancellationToken.None, width, height).ConfigureAwait(false);
            try { await DownloadMetadata.WriteAsync(destinationPath, job, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning("Видео сохранено, но не удалось записать метаданные: {ErrorType}", ex.GetType().Name); }
        }
        catch (MirrorAccessException ex) when (ex.StatusCode == 500)
        {
            _stoppedByMirror = true;
            logger.LogError("Зеркало HDrezka вернуло HTTP 500. Загрузки остановлены.");
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Ошибка загрузки HDrezka: {ErrorType}, статус зеркала: {Status}", ex.GetType().Name, (ex as MirrorAccessException)?.StatusCode);
            throw;
        }
        var destination = DownloadLibraries.List(library.GetVirtualFolders(), media.Root).FirstOrDefault(item => item.Id == job.LibraryId);
        var destinationFolder = library.GetVirtualFolders().FirstOrDefault(item => item.Name == destination?.Name);
        if (destination is not null && !Guid.TryParse(destinationFolder?.ItemId, out _))
        {
            try
            {
                await library.ValidateTopLibraryFolders(token, false).ConfigureAwait(false);
                destinationFolder = library.GetVirtualFolders().FirstOrDefault(item => item.Name == destination.Name);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning("Файл загружен, но медиатека не обновлена: {ErrorType}", ex.GetType().Name); }
        }
        if (Guid.TryParse(destinationFolder?.ItemId, out var libraryId) && library.GetItemById(libraryId) is Folder folder)
        {
            try { await ScanFolderAsync(library, folder, root, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogWarning("Файл загружен, но медиатека не обновлена: {ErrorType}", ex.GetType().Name); }
        }
        else logger.LogWarning("Файл загружен; медиатека ожидает обновления в Jellyfin.");
        }
        finally { _active.TryRemove(job.Id, out _); _progress.TryRemove(job.Id, out _); }
    }

    private static async Task ScanFolderAsync(ILibraryManager library, Folder folder, string root, CancellationToken token)
    {
        var options = new MediaBrowser.Controller.Providers.MetadataRefreshOptions(new MediaBrowser.Controller.Providers.DirectoryService(BaseItem.FileSystem))
        { EnableRemoteContentProbe = false };
        var physical = library.FindByPath(root, true) as Folder;
        if (physical is null)
        {
            await library.ValidateTopLibraryFolders(token, false).ConfigureAwait(false);
            physical = library.FindByPath(root, true) as Folder;
        }
        if (physical is null) throw new InvalidOperationException("Jellyfin ещё не создал физическую папку медиатеки.");
        await physical.ValidateChildren(new Progress<double>(), options, true, false, token).ConfigureAwait(false);
        await folder.RefreshMetadata(token).ConfigureAwait(false);
    }

    public override void Dispose() { _mirror?.Dispose(); _cdnClient.Dispose(); base.Dispose(); }
}
