using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class ShortLivedStreamCacheTests
{
    [Fact]
    public async Task ReusesOnlySuccessfulFreshResultsAndCoalescesConcurrentRequests()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new ShortLivedStreamCache(TimeSpan.FromMinutes(2), () => now);
        var calls = 0;
        async Task<IReadOnlyList<NamedStream>> Fetch(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(20, ct);
            return [new NamedStream("voice", new VideoStream(720, new Uri("https://example.test/stream")))];
        }
        await Task.WhenAll(cache.GetAsync("mirror-a/item/voice", Fetch, CancellationToken.None), cache.GetAsync("mirror-a/item/voice", Fetch, CancellationToken.None));
        Assert.Equal(1, calls);
        await cache.GetAsync("mirror-b/item/voice", Fetch, CancellationToken.None);
        Assert.Equal(2, calls);
        now += TimeSpan.FromMinutes(3);
        await cache.GetAsync("mirror-a/item/voice", Fetch, CancellationToken.None);
        Assert.Equal(3, calls);
        cache.Invalidate("mirror-a/item/voice");
        await cache.GetAsync("mirror-a/item/voice", Fetch, CancellationToken.None);
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task EmptyAndFailedResultsAreNotCached()
    {
        var cache = new ShortLivedStreamCache(TimeSpan.FromMinutes(2));
        var calls = 0;
        Task<IReadOnlyList<NamedStream>> Empty(CancellationToken _) { calls++; return Task.FromResult<IReadOnlyList<NamedStream>>([]); }
        await cache.GetAsync("x", Empty, CancellationToken.None);
        await cache.GetAsync("x", Empty, CancellationToken.None);
        Assert.Equal(2, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetAsync("x", _ => throw new InvalidOperationException(), CancellationToken.None));
        await cache.GetAsync("x", Empty, CancellationToken.None);
        Assert.Equal(3, calls);
    }
}
