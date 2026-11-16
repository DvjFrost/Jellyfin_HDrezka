using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbRetryTests
{
    [Fact]
    public async Task DoesNotRetryMissingOrUnauthorizedTitles()
    {
        foreach (var code in new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden })
        {
            var calls = 0;
            await Assert.ThrowsAsync<HttpRequestException>(() => TmdbRetry.RunAsync<int>(_ => { calls++; throw new HttpRequestException("failed", null, code); }, CancellationToken.None, TimeSpan.Zero));
            Assert.Equal(1, calls);
        }
    }

    [Fact]
    public async Task RetriesTemporaryRateLimitsAndStopsAfterSuccess()
    {
        var calls = 0;
        var result = await TmdbRetry.RunAsync<int>(_ => { calls++; if (calls < 3) throw new HttpRequestException("limited", null, HttpStatusCode.TooManyRequests); return Task.FromResult(42); }, CancellationToken.None, TimeSpan.Zero);
        Assert.Equal(42, result); Assert.Equal(3, calls);
    }
}
