using System.Globalization;

namespace Jellyfin.Plugin.Hdrezka;

public static class DownloadProcessor
{
    public static async Task<int> ExecuteAsync(DownloadJob job, string libraryRoot, MirrorClient mirror, HttpClient cdnClient, string? cdnHost, CancellationToken token, Action<string>? diagnostic = null, Action<long, long?>? progress = null, HdrezkaSource? source = null)
    {
        var destination = DownloadPaths.Resolve(libraryRoot, job.RelativePath);
        if (File.Exists(destination)) throw new IOException("Файл уже существует.");
        source ??= new HdrezkaSource(mirror);
        var listing = await source.VoiceListingAsync(job.ContentPath, token).ConfigureAwait(false);
        var voice = listing.Voices.FirstOrDefault(v => v.Id == job.VoiceId) ?? throw new InvalidOperationException("Бесплатная озвучка недоступна.");
        IReadOnlyList<NamedStream> streams;
        if ((job.Season is null && job.Episode is null) || job.UseMoviePlayer)
            streams = await source.MovieVoiceAlternativesFromListingAsync(job.ContentPath, voice, token).ConfigureAwait(false);
        else if (job.Season is >= 0 && job.Episode is >= 1)
            streams = await source.EpisodeAlternativesFromListingAsync(job.ContentPath, voice, job.Season.Value.ToString(CultureInfo.InvariantCulture), job.Episode.Value.ToString(CultureInfo.InvariantCulture), token).ConfigureAwait(false);
        else throw new ArgumentException("Неверный номер сезона или серии.");
        diagnostic?.Invoke("Получено вариантов: " + streams.Count.ToString(CultureInfo.InvariantCulture) + "; серверы: " + string.Join(", ", streams.Select(s => s.Stream.Url.Host).Distinct(StringComparer.OrdinalIgnoreCase)));
        var limit = job.Quality is > 0 and <= 1080 ? job.Quality : 1080;
        foreach (var stream in streams.Where(s => s.Stream.Quality is > 0 and <= 1080 && s.Stream.Quality <= limit &&
                     !s.Stream.Url.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                 .OrderByDescending(s => s.Stream.Quality)
                 .ThenByDescending(s => s.Stream.Url.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)))
        {
            if (!CdnSelection.IsKnownDownloadHost(stream.Stream.Url)) { diagnostic?.Invoke("Неизвестный сервер видео: " + stream.Stream.Url.Host); continue; }
            try
            {
                await DirectMp4Downloader.DownloadAsync(cdnClient, CdnSelection.Apply(stream.Stream.Url, cdnHost), destination, token, mirror.CurrentMirror, diagnostic: diagnostic, progress: progress, jobId: job.Id).ConfigureAwait(false);
                return stream.Stream.Quality;
            }
            catch (InvalidDataException ex) { diagnostic?.Invoke("Ответ не подошёл: " + ex.Message); }
            catch (HttpRequestException ex) { diagnostic?.Invoke("Сервер видео недоступен: " + (ex.StatusCode?.ToString() ?? ex.GetType().Name)); }
            catch (TimeoutException) { diagnostic?.Invoke("Сервер видео не отвечает вовремя."); }
        }
        throw new InvalidDataException("Не найден доступный прямой MP4 выбранного качества.");
    }
}
