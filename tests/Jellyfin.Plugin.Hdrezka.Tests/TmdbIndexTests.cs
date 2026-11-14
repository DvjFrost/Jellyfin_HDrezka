using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbIndexTests
{
    [Fact]
    public async Task ResumesWithoutRepeatingCompletedEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-index-" + Guid.NewGuid().ToString("N"));
        try
        {
            var calls = 0;
            using var http = new HttpClient(new Handler(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[]}") }; }));
            var store = new TmdbMatchStore(Path.Combine(root, "matches.jsonl"));
            var index = new TmdbIndex(new TmdbMatcher(new TmdbClient(http, "token"), TimeSpan.Zero), store);
            var videos = new[] { new SitemapVideo("1", "test", 2020, CatalogKind.Movie, new Uri("https://test.invalid/films/a/1-test-2020.html")) };
            await index.RunAsync(videos, CancellationToken.None);
            var firstCalls = calls;
            var callbacks = 0;
            var result = await index.RunAsync(videos, CancellationToken.None, onItem: (_, _, _) => { callbacks++; return Task.CompletedTask; });
            Assert.Equal(firstCalls, calls);
            Assert.Equal(1, result.NoResults);
            Assert.Equal(1, callbacks);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProgressReportsEntriesActuallyVisitedEvenWhenMatchesAreCached()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-progress-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TmdbMatchStore(Path.Combine(root, "matches.jsonl"));
            var videos = Enumerable.Range(1, 200).Select(i => new SitemapVideo(i.ToString(), "test " + i, 2020, CatalogKind.Movie, new Uri($"https://test.invalid/films/a/{i}-test.html"))).ToArray();
            foreach (var video in videos) await store.AppendAsync(new TmdbMatch(video.Id, TmdbMatchStatus.NoResults, null, null, null, null, TmdbMatcher.Fingerprint(video)), CancellationToken.None);
            using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Cached matches must not make requests")));
            var reports = new List<TmdbIndexSummary>();
            await new TmdbIndex(new TmdbMatcher(new TmdbClient(http, "token"), TimeSpan.Zero), store).RunAsync(videos, CancellationToken.None, new InlineProgress(reports));
            Assert.Equal(100, reports[0].Processed);
            Assert.Equal(200, reports[0].Total);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class InlineProgress(List<TmdbIndexSummary> reports) : IProgress<TmdbIndexSummary>
    {
        public void Report(TmdbIndexSummary value) => reports.Add(value);
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
