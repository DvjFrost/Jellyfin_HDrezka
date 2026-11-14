using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class CatalogStatisticsTests
{
    [Fact]
    public void CountsOnlyCurrentMovieAndSeriesResults()
    {
        var movie = new SitemapVideo("1", "one", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/1-one.html"));
        var series = new SitemapVideo("2", "two", 2020, CatalogKind.Series, new Uri("https://mirror.test/series/a/2-two.html"));
        var anime = new SitemapVideo("3", "three", 2020, CatalogKind.Anime, new Uri("https://mirror.test/animation/a/3-three.html"));
        var matches = new Dictionary<string, TmdbMatch> {
            ["1"] = new("1", TmdbMatchStatus.Matched, 1, "Title", null, "movie", TmdbMatcher.Fingerprint(movie)),
            ["2"] = new("2", TmdbMatchStatus.NoResults, null, null, null, null, "stale"),
            ["3"] = new("3", TmdbMatchStatus.Matched, 3, "Anime", null, "tv", TmdbMatcher.Fingerprint(anime))
        };
        var stats = CatalogStatistics.Calculate([movie, series, anime], matches);
        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.Processed);
        Assert.Equal(1, stats.Matched);
        Assert.Equal(1, stats.Remaining);
    }
}
