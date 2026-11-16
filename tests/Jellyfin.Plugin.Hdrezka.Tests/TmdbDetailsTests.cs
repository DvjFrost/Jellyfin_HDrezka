using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbDetailsTests
{
    [Fact]
    public async Task FetchesAndParsesDetailsAndCreditsWithOneRequest()
    {
        const string json = """{"id":456,"title":"Название","original_title":"Original","overview":"Описание","tagline":"Слоган","vote_average":7.5,"genres":[{"name":"Драма"}],"credits":{"cast":[{"name":"Актёр","character":"Герой"}],"crew":[{"name":"Режиссёр","job":"Director"}]},"poster_path":"/cover.jpg","backdrop_path":"/wide.jpg"}""";
        var handler = new Handler(json); using var http = new HttpClient(handler);
        var result = await new TmdbClient(http, "test-token").DetailsAsync(456, false, CancellationToken.None);
        Assert.Equal(1, handler.Calls); Assert.Contains("append_to_response=credits", handler.Path);
        Assert.Equal("Описание", result.Overview); Assert.Equal("Original", result.OriginalTitle);
        Assert.Equal("Драма", Assert.Single(result.Genres)); Assert.Equal("Актёр", Assert.Single(result.Actors).Name);
        Assert.Equal("Герой", result.Actors[0].Role); Assert.Equal("Режиссёр", Assert.Single(result.Directors));
        Assert.Equal(7.5, result.TmdbRating);
    }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Path { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; Path = request.RequestUri!.PathAndQuery; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }); }
    }
}
