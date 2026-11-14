using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class CatalogRetryTests
{
    [Fact]
    public async Task DoesNotRetryMirrorHttp500()
    {
        var calls = 0;
        await Assert.ThrowsAsync<MirrorAccessException>(() => CatalogRetry.RunAsync<int>(_ => { calls++; throw new MirrorAccessException("server error", 500); }, CancellationToken.None, TimeSpan.FromMilliseconds(1)));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RetriesTransientIoFailure()
    {
        var calls = 0;
        var result = await CatalogRetry.RunAsync<int>(_ => ++calls == 1 ? throw new IOException("connection reset") : Task.FromResult(42), CancellationToken.None, TimeSpan.FromMilliseconds(1));
        Assert.Equal(42, result);
        Assert.Equal(2, calls);
    }
    [Fact]
    public async Task RetriesTemporaryAccessErrorAndRespectsCancellation()
    {
        var calls = 0;
        var value = await CatalogRetry.RunAsync<int>(_ => ++calls == 1 ? throw new MirrorAccessException("blocked", 403) : Task.FromResult(7), CancellationToken.None, TimeSpan.FromMilliseconds(1));
        Assert.Equal(7, value);
        Assert.Equal(2, calls);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CatalogRetry.RunAsync<int>(_ => Task.FromResult(7), cancelled.Token, TimeSpan.FromMilliseconds(1)));
    }
}
