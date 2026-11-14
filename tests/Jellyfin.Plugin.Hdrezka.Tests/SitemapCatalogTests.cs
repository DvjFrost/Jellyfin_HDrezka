using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SitemapCatalogTests
{
    [Fact]
    public void ParsesVideoIdentityAndReplacesUntrustedHost()
    {
        const string xml = "<urlset xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'><url><loc>https://rezka.ag/films/drama/93616-dnevnik-gornichnoy-2026.html</loc></url><url><loc>https://rezka.ag/films/</loc></url></urlset>";
        var items = SitemapCatalog.Parse(xml, new Uri("https://mirror.test/"));
        var item = Assert.Single(items);
        Assert.Equal("93616", item.Id);
        Assert.Equal("dnevnik gornichnoy", item.Query);
        Assert.Equal(2026, item.Year);
        Assert.Equal(CatalogKind.Movie, item.Kind);
        Assert.Equal("https://mirror.test/films/drama/93616-dnevnik-gornichnoy-2026.html", item.Url.AbsoluteUri);
    }

    [Fact]
    public void RecognizesShowsAndAnimationAndRejectsMalformedPaths()
    {
        const string xml = "<urlset><url><loc>https://rezka.ag/series/drama/123-show-2020.html</loc></url><url><loc>https://rezka.ag/cartoons/fantasy/456-story-2019.html</loc></url><url><loc>https://rezka.ag/animation/a/789-name-2021.html</loc></url><url><loc>https://rezka.ag/show/reality-shows/999-show-2020.html</loc></url><url><loc>https://rezka.ag/series/bogus.html</loc></url></urlset>";
        var items = SitemapCatalog.Parse(xml, new Uri("https://mirror.test/"));
        Assert.Equal(new[] { CatalogKind.Series, CatalogKind.Cartoon, CatalogKind.Anime, CatalogKind.Show }, items.Select(x => x.Kind));
    }

    [Fact]
    public void SitemapIndexUsesMirrorAndAcceptsOnlySitemapXmlPaths()
    {
        const string xml = "<sitemapindex><sitemap><loc>https://hdrezka.ag/uploads/sitemap2.xml</loc></sitemap><sitemap><loc>https://example.test/uploads/sitemap3.xml?x=1</loc></sitemap><sitemap><loc>https://evil.test/admin</loc></sitemap></sitemapindex>";
        var result = SitemapCatalog.ParseIndex(xml, new Uri("https://mirror.test/"));
        Assert.Equal("https://mirror.test/uploads/sitemap2.xml", Assert.Single(result).AbsoluteUri);
    }

    [Fact]
    public void IgnoresLatestSuffixWhenExtractingTitleAndYear()
    {
        const string xml = "<urlset><url><loc>https://rezka.ag/series/drama/92807-noktyurn-2026-latest.html</loc></url></urlset>";
        var video = Assert.Single(SitemapCatalog.Parse(xml, new Uri("https://mirror.test/")));
        Assert.Equal("noktyurn", video.Query);
        Assert.Equal(2026, video.Year);
        Assert.EndsWith("-2026-latest.html", video.Url.AbsolutePath);
    }
}
