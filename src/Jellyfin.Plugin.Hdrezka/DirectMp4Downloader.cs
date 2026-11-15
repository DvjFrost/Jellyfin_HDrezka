using System.Net;

namespace Jellyfin.Plugin.Hdrezka;

public static class DirectMp4Downloader
{
    public static async Task DownloadAsync(HttpClient client, Uri url, string destination, CancellationToken token, Uri? referer = null, TimeSpan? idleTimeout = null, Action<string>? diagnostic = null, Action<long, long?>? progress = null, Guid? jobId = null)
    {
        var timeout = idleTimeout ?? TimeSpan.FromSeconds(20);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(url.UserInfo))
            throw new ArgumentException("Недопустимая ссылка на видео.", nameof(url));
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? throw new ArgumentException("Не указана папка.", nameof(destination));
        Directory.CreateDirectory(directory);
        if (File.Exists(destination)) throw new IOException("Файл уже существует.");
        var temporary = Path.Combine(directory, ".hdrezka-" + (jobId ?? Guid.NewGuid()).ToString("N") + ".part");
        try
        {
            if (jobId is not null && File.Exists(temporary)) File.Delete(temporary);
            using var response = await GetResponseAsync(client, url, referer, timeout, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Сервер видео вернул HTTP " + (int)response.StatusCode + ".");
            var length = response.Content.Headers.ContentLength;
            diagnostic?.Invoke("Ответ видео: тип " + (response.Content.Headers.ContentType?.MediaType ?? "не указан") + ", размер " + (length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "не указан"));
            if (length is < 12) throw new InvalidDataException("Слишком короткий ответ сервера видео.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var header = new byte[12];
                var headerLength = await ReadWithTimeoutAsync(input, header, timeout, token, true).ConfigureAwait(false);
                if (headerLength != header.Length || header[4] != 'f' || header[5] != 't' || header[6] != 'y' || header[7] != 'p')
                    throw new InvalidDataException(headerLength >= 7 && System.Text.Encoding.ASCII.GetString(header, 0, 7) == "#EXTM3U"
                        ? "Сервер вернул HLS-плейлист вместо MP4." : "Ответ сервера не является MP4.");
                diagnostic?.Invoke("Заголовок MP4 подтверждён.");
                await output.WriteAsync(header, token).ConfigureAwait(false);
                long written = header.Length;
                progress?.Invoke(written, length);
                var buffer = new byte[81920];
                int read;
                while ((read = await ReadWithTimeoutAsync(input, buffer, timeout, token, false).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    written += read;
                    progress?.Invoke(written, length);
                }
                if (length is not null && output.Length != length.Value) throw new InvalidDataException("Видео загружено не полностью.");
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            File.Move(temporary, destination, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<int> ReadWithTimeoutAsync(Stream input, Memory<byte> buffer, TimeSpan timeout, CancellationToken token, bool all)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            return all ? await input.ReadAtLeastAsync(buffer, buffer.Length, false, limit.Token).ConfigureAwait(false)
                : await input.ReadAsync(buffer, limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Сервер видео не отвечает."); }
    }

    private static async Task<HttpResponseMessage> GetResponseAsync(HttpClient client, Uri initial, Uri? referer, TimeSpan timeout, CancellationToken token)
    {
        var current = initial;
        for (var i = 0; i < 6; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.TryAddWithoutValidation("User-Agent", MirrorClient.Agent);
            if (referer is not null) request.Headers.Referrer = referer;
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(timeout);
            HttpResponseMessage response;
            try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Сервер видео не отвечает."); }
            if ((int)response.StatusCode is not (>= 300 and <= 399) || response.Headers.Location is null) return response;
            var location = response.Headers.Location;
            var next = location.IsAbsoluteUri ? location : new Uri(current, location);
            response.Dispose();
            if (!CdnSelection.IsKnownDownloadHost(next)) throw new InvalidDataException("Неподтверждённый сервер видео: " + next.Host);
            current = next;
        }
        throw new InvalidDataException("Слишком много перенаправлений видео.");
    }
}
