using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SitemapChangesTests
{
    [Fact]
    public void OnlyNewChangedOrMissingEntriesArePrepared()
    {
        var a = new SitemapVideo("1", "one", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/1-one-2020.html"));
        var b = new SitemapVideo("2", "two", 2020, CatalogKind.Series, new Uri("https://mirror.test/series/a/2-two-2020.html"));
        var old = new[] { a, b }.ToDictionary(x => x.Id);
        Assert.False(SitemapChanges.NeedsWrite(a, old, true));
        Assert.True(SitemapChanges.NeedsWrite(a, old, false));
        Assert.True(SitemapChanges.NeedsWrite(b with { Query = "changed" }, old, true));
        Assert.True(SitemapChanges.NeedsWrite(a with { Id = "3" }, old, true));
    }
}
