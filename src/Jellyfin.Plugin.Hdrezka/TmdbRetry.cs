using System.Net;

namespace Jellyfin.Plugin.Hdrezka;

public static class TmdbRetry
{
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, TimeSpan? initialDelay = null)
    {
        var delay = initialDelay ?? TimeSpan.FromSeconds(3);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return await action(token).ConfigureAwait(false); }
            catch (HttpRequestException ex) when (attempt < 3 && (ex.StatusCode is null or HttpStatusCode.TooManyRequests || (int)ex.StatusCode >= 500))
            { await Task.Delay(delay * (attempt + 1), token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (attempt < 3 && !token.IsCancellationRequested)
            { await Task.Delay(delay * (attempt + 1), token).ConfigureAwait(false); }
        }
    }
}
