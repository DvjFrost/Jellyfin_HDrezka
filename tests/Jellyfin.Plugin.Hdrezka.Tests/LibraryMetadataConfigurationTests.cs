using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Model.Configuration;
using Xunit;

public sealed class LibraryMetadataConfigurationTests
{
    [Fact]
    public void SeriesConfigurationEnablesIdentificationWithoutEnablingEpisodeProviders()
    {
        var options = new LibraryOptions { TypeOptions = [new TypeOptions { Type = "Series", MetadataFetchers = ["Custom"] }] };
        SitemapLibraries.ConfigureMetadata(options, CatalogKind.Series);
        var series = options.TypeOptions.Single(x => x.Type == "Series");
        Assert.Contains("Custom", series.MetadataFetchers);
        Assert.Contains("TheMovieDb", series.MetadataFetchers);
        Assert.Contains("Nfo", options.MetadataSavers);
        Assert.True(options.SaveLocalMetadata);
        Assert.True(options.EnableInternetProviders);
        Assert.Equal(new[] { "HDrezka" }, options.TypeOptions.Single(x => x.Type == "Episode").MetadataFetchers);
        Assert.Equal(new[] { "HDrezka" }, options.TypeOptions.Single(x => x.Type == "Season").ImageFetchers);
        SitemapLibraries.ConfigureMetadata(options, CatalogKind.Series);
        Assert.Single(series.MetadataFetchers, x => x == "TheMovieDb");
    }
}
