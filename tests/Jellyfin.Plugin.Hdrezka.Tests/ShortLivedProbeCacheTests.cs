using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class ShortLivedProbeCacheTests
{
    [Fact]
    public async Task SharesOnlySuccessfulProbeForSameUrlAndExpires()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new ShortLivedProbeCache<string>(TimeSpan.FromMinutes(2), () => now);
        var calls = 0;
        Task<string?> Probe(CancellationToken _) { calls++; return Task.FromResult<string?>("ok"); }
        Assert.Equal("ok", await cache.GetAsync("url1", Probe, x => x == "ok", CancellationToken.None));
        Assert.Equal("ok", await cache.GetAsync("url1", Probe, x => x == "ok", CancellationToken.None));
        Assert.Equal(1, calls);
        await cache.GetAsync("url2", Probe, x => x == "ok", CancellationToken.None);
        Assert.Equal(2, calls);
        now = now.AddMinutes(3);
        await cache.GetAsync("url1", Probe, x => x == "ok", CancellationToken.None);
        Assert.Equal(3, calls);
        Task<string?> Fail(CancellationToken _) { calls++; return Task.FromResult<string?>(null); }
        await cache.GetAsync("url3", Fail, x => x == "ok", CancellationToken.None);
        await cache.GetAsync("url3", Fail, x => x == "ok", CancellationToken.None);
        Assert.Equal(5, calls);
    }
}
