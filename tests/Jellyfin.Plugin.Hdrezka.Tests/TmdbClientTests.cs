using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbClientTests
{
    [Fact]
    public async Task SendsTokenOnlyToTmdbAndReadsRussianResults()
    {
        var handler = new Handler(req => {
            Assert.Equal("api.themoviedb.org", req.RequestUri!.Host);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("secret", req.Headers.Authorization.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"results\":[{\"id\":123,\"title\":\"Тест\",\"original_title\":\"Test\",\"release_date\":\"2020-01-01\",\"poster_path\":\"/a.jpg\"}]}") };
        });
        using var http = new HttpClient(handler);
        var result = await new TmdbClient(http, "secret").SearchAsync("test", false, CancellationToken.None);
        Assert.Equal("Тест", Assert.Single(result).Title);
    }

    [Fact]
    public async Task FailureIsNotAnEmptySearchResult()
    {
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new TmdbClient(http, "secret").SearchAsync("test", false, CancellationToken.None));
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
