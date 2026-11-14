namespace Jellyfin.Plugin.Hdrezka;

public static class CatalogRetry
{
    public static async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken token, TimeSpan? initialDelay = null)
    {
        var delay = initialDelay ?? TimeSpan.FromSeconds(15);
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return await action(token).ConfigureAwait(false); }
            catch (MirrorAccessException ex) when (attempt < 3 && (ex.StatusCode is 403 or 429 or >= 501 and <= 599))
            {
                await Task.Delay(delay * (attempt + 1), token).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await Task.Delay(delay * (attempt + 1), token).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < 3)
            {
                await Task.Delay(delay * (attempt + 1), token).ConfigureAwait(false);
            }
        }
    }
}
