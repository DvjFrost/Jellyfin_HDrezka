using System.Net;
using System.Text;
using Jellyfin.Plugin.Hdrezka;
using Microsoft.Extensions.Logging;
using Xunit;

public sealed class MirrorClientTests
{
    [Fact]
    public async Task LogsChallengeOutcomeWithoutSecrets()
    {
        var handler = new SequenceHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Challenge) },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") },
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") });
        using var client = new HttpClient(handler);
        var log = new RecordingLogger();
        using var mirror = new MirrorClient(new Uri("https://anubis-log.example/"), client, logger: log);
        Assert.Equal("ok", await mirror.GetPageAsync(new Uri("https://anubis-log.example/item"), CancellationToken.None));
        Assert.Contains(log.Messages, x => x.Contains("Anubis") && x.Contains("detected"));
        Assert.Contains(log.Messages, x => x.Contains("Anubis") && x.Contains("passed"));
        Assert.DoesNotContain(log.Messages, x => x.Contains("hunter") || x.Contains("abc") || x.Contains("nonce"));
    }

    [Fact]
    public async Task FailedChallengeIsNotLoggedAsPassed()
    {
        var handler = new SequenceHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Challenge.Replace("fast", "unsupported")) });
        using var client = new HttpClient(handler);
        var log = new RecordingLogger();
        using var mirror = new MirrorClient(new Uri("https://anubis-fail-log.example/"), client, logger: log);
        await Assert.ThrowsAsync<MirrorAccessException>(() => mirror.GetPageAsync(new Uri("https://anubis-fail-log.example/item"), CancellationToken.None));
        Assert.Contains(log.Messages, x => x.Contains("detected"));
        Assert.DoesNotContain(log.Messages, x => x.Contains("passed"));
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public async Task Http500StopsFurtherRequestsAcrossClientsForSameMirror()
    {
        var calls = 0;
        using var client = new HttpClient(new Counting500Handler(() => Interlocked.Increment(ref calls)));
        var uri = new Uri("https://stop-after-500.example/");
        using var first = new MirrorClient(uri, client);
        using var second = new MirrorClient(uri, client);
        var error = await Assert.ThrowsAsync<MirrorAccessException>(() => first.GetPageAsync(uri, CancellationToken.None));
        Assert.Equal(500, error.StatusCode);
        var blocked = await Assert.ThrowsAsync<MirrorAccessException>(() => second.GetPageAsync(uri, CancellationToken.None));
        Assert.Equal(500, blocked.StatusCode);
        Assert.Equal(1, calls);
    }

    private sealed class Counting500Handler(Action onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        }
    }
    [Fact]
    public async Task ConcurrentChallengesShareOneRenewal()
    {
        var checks = 0;
        using var handler = new AsyncHandler(async request => {
            await Task.Yield();
            if (request.RequestUri!.AbsolutePath.Contains("pass-challenge")) {
                Interlocked.Increment(ref checks);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
            }
            if (Volatile.Read(ref checks) == 0) {
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
        using var mirror = new MirrorClient(new Uri("https://immediate-500.test/"), client);
        var error = await Assert.ThrowsAsync<MirrorAccessException>(() => mirror.GetPageAsync(new Uri("https://immediate-500.test/one"), CancellationToken.None));
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
