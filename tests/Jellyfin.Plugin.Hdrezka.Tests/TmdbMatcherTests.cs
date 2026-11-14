using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbMatcherTests
{
    [Fact]
    public async Task DistinguishesNoResultsFromUncertainAndMatch()
    {
        var handler = new Handler(req => {
            var query = Uri.UnescapeDataString(req.RequestUri!.Query);
            var body = query.Contains("empty") || query.Contains("емптй") ? "{\"results\":[]}" : query.Contains("other")
                ? "{\"results\":[{\"id\":2,\"title\":\"Other name\",\"release_date\":\"2020-01-01\"}]}"
                : "{\"results\":[{\"id\":1,\"title\":\"Test\",\"original_title\":\"Test\",\"release_date\":\"2020-01-01\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        });
        using var http = new HttpClient(handler);
        var matcher = new TmdbMatcher(new TmdbClient(http, "secret"), TimeSpan.Zero);
        SitemapVideo Video(string query) => new("1", query, 2020, CatalogKind.Movie, new Uri("https://example.test/films/a/1-test-2020.html"));
        Assert.Equal(TmdbMatchStatus.Matched, (await matcher.MatchAsync(Video("test"), CancellationToken.None)).Status);
        Assert.Equal(TmdbMatchStatus.Uncertain, (await matcher.MatchAsync(Video("other"), CancellationToken.None)).Status);
        Assert.Equal(TmdbMatchStatus.NoResults, (await matcher.MatchAsync(Video("empty"), CancellationToken.None)).Status);
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
