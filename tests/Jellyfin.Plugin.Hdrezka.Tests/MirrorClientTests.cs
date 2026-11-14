using System.Net;
using System.Text;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MirrorClientTests
{
    [Fact]
    public async Task ConcurrentChallengesShareOneRenewal()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pages = 0; var checks = 0;
        using var handler = new AsyncHandler(async request => {
            if (request.RequestUri!.AbsolutePath.Contains("pass-challenge")) {
                Interlocked.Increment(ref checks);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            }
            var count = Interlocked.Increment(ref pages);
            if (count <= 2) {
                if (count == 2) barrier.TrySetResult();
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(3));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Challenge) };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        });
        using var http = new HttpClient(handler);
        using var one = new MirrorClient(new Uri("https://concurrent.test/"), http);
        using var two = new MirrorClient(new Uri("https://concurrent.test/"), http);
        var results = await Task.WhenAll(one.GetPageAsync(new Uri("https://concurrent.test/one"), CancellationToken.None), two.GetPageAsync(new Uri("https://concurrent.test/two"), CancellationToken.None));
        Assert.Equal(new[] { "ok", "ok" }, results);
        Assert.Equal(1, checks);
    }

    private sealed class AsyncHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request);
    }
    [Fact]
    public async Task Http500StopsImmediatelyEvenWhenResponseContainsChallenge()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(Challenge) });
        using var client = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), client);
        var error = await Assert.ThrowsAsync<MirrorAccessException>(() => mirror.GetPageAsync(new Uri("https://example.test/one"), CancellationToken.None));
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task SitemapCanExceedTheOrdinaryPageLimit()
    {
        var body = new string('x', 2_100_000);
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        using var client = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), client);
        Assert.Equal(body.Length, (await mirror.GetSitemapAsync(new Uri("https://example.test/sitemap.xml"), CancellationToken.None)).Length);
    }
    [Fact]
    public async Task CatalogRequestsCanBeRateLimited()
    {
        var times = new List<DateTime>();
        HttpResponseMessage Reply(HttpRequestMessage request) { times.Add(DateTime.UtcNow); return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }; }
        var handler = new SequenceHandler(Reply, Reply);
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http, TimeSpan.FromMilliseconds(80));
        await mirror.GetPageAsync(new Uri("https://example.test/one"), CancellationToken.None);
        await mirror.GetPageAsync(new Uri("https://example.test/two"), CancellationToken.None);
        Assert.True(times[1] - times[0] >= TimeSpan.FromMilliseconds(50));
    }
    private const string Challenge = "<script id='anubis_challenge'>{\"rules\":{\"algorithm\":\"fast\",\"difficulty\":0},\"challenge\":{\"id\":\"abc\",\"randomData\":\"hunter\"}}</script>";

    [Fact]
    public async Task CompletesAnubisAndRetriesOriginalPage()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Challenge) },
            req => {
                Assert.Equal("/.within.website/x/cmd/anubis/api/pass-challenge", req.RequestUri!.AbsolutePath);
                Assert.Contains("nonce=0", req.RequestUri.Query);
                Assert.Contains("response=2652bdba", req.RequestUri.Query);
                return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("/films/example", UriKind.Relative) } };
            },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<div class='b-content__inline_item'>ok</div>") }
        );
        using var client = new HttpClient(handler);
        var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), client);
        var page = await mirror.GetPageAsync(new Uri("https://rezka-tv.org/films/example"), CancellationToken.None);
        Assert.Contains("b-content__inline_item", page);
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task RefusesRedirectToUnrelatedOrigin()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/") } });
        using var client = new HttpClient(handler);
        var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), client);
        await Assert.ThrowsAsync<MirrorAccessException>(() => mirror.GetPageAsync(new Uri("https://rezka-tv.org/"), CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task DoesNotReportUnknownAnubisChallengeAsCatalogue()
    {
        var html = Challenge.Replace("fast", "unsupported");
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        using var client = new HttpClient(handler);
        var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), client);
        await Assert.ThrowsAsync<MirrorAccessException>(() => mirror.GetPageAsync(new Uri("https://rezka-tv.org/"), CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    private sealed class SequenceHandler(params Func<HttpRequestMessage,HttpResponseMessage>[] replies) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Count >= replies.Length) throw new InvalidOperationException("Unexpected request");
            return Task.FromResult(replies[Count++](request));
        }
    }
}
