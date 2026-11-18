using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class MirrorSessionRefreshService(ILogger<MirrorSessionRefreshService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var url = Plugin.Instance?.Configuration.MirrorUrl;
                    if (Uri.TryCreate(url, UriKind.Absolute, out var mirror) && mirror.Scheme == Uri.UriSchemeHttps)
                        await MirrorClient.ResetSessionAsync(mirror, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Не удалось обновить сессию HDrezka"); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
