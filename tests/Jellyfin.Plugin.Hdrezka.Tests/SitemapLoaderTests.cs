using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SitemapLoaderTests
{
    [Fact]
    public async Task CatalogContainsOnlyFilmsAndSeries()
    {
        var handler = new Handler(req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.RequestUri!.AbsolutePath == "/sitemap.xml"
            ? "<sitemapindex><sitemap><loc>https://rezka.ag/uploads/sitemap2.xml</loc></sitemap></sitemapindex>"
            : "<urlset><url><loc>https://rezka.ag/films/a/1-a.html</loc></url><url><loc>https://rezka.ag/series/a/2-b.html</loc></url><url><loc>https://rezka.ag/animation/a/3-c.html</loc></url><url><loc>https://rezka.ag/cartoons/a/4-d.html</loc></url><url><loc>https://rezka.ag/show/a/5-e.html</loc></url></urlset>") });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
        var result = await SitemapLoader.LoadAsync(mirror, CancellationToken.None);
        Assert.Equal(new[] { CatalogKind.Movie, CatalogKind.Series }, result.Select(x => x.Kind));
    }
    [Fact]
    public async Task FetchesOnlySitemapsAndUsesCurrentMirror()
    {
        var paths = new List<string>();
        var handler = new Handler(req => {
            paths.Add(req.RequestUri!.AbsolutePath);
            var xml = paths.Count == 1
                ? "<sitemapindex><sitemap><loc>https://rezka.ag/uploads/sitemap2.xml</loc></sitemap></sitemapindex>"
                : "<urlset><url><loc>https://rezka.ag/films/drama/123-test-2020.html</loc></url></urlset>";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) };
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
        var result = await SitemapLoader.LoadAsync(mirror, CancellationToken.None);
        Assert.Single(result);
        Assert.Equal(new[] { "/sitemap.xml", "/uploads/sitemap2.xml" }, paths);
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
