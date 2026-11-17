using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Model.Configuration;
using Xunit;

public sealed class LibraryMetadataConfigurationTests
{
    [Fact]
    public void StartupMigrationRecognizesOnlyOwnedLibraries()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-owned-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(CatalogKind.Movie, SitemapLibraries.OwnedKind("Фильмы HDrezka", [Path.Combine(root, "films")], root));
        Assert.Equal(CatalogKind.Series, SitemapLibraries.OwnedKind("Сериалы HDrezka", [Path.Combine(root, "series")], root));
        Assert.Null(SitemapLibraries.OwnedKind("Фильмы HDrezka", [Path.Combine(root, "other")], root));
        Assert.Null(SitemapLibraries.OwnedKind("Другая медиатека", [Path.Combine(root, "films")], root));
    }

    [Fact]
    public void StartupMigrationLeavesOtherLibrarySettingsIntact()
    {
        var options = new LibraryOptions { EnableInternetProviders = false, SaveLocalMetadata = false, MetadataSavers = ["Nfo", "Custom"] };
        SitemapLibraries.ConfigureNfoSaver(options);
        Assert.False(options.EnableInternetProviders);
        Assert.False(options.SaveLocalMetadata);
        Assert.Equal(new[] { "Custom", "HDrezka NFO" }, options.MetadataSavers);
    }

    [Fact]
    public void SeriesConfigurationEnablesIdentificationWithoutEnablingEpisodeProviders()
    {
        var options = new LibraryOptions { MetadataSavers = ["Nfo", "Custom"], TypeOptions = [new TypeOptions { Type = "Series", MetadataFetchers = ["Custom"] }] };
        SitemapLibraries.ConfigureMetadata(options, CatalogKind.Series);
        var series = options.TypeOptions.Single(x => x.Type == "Series");
        Assert.Contains("Custom", series.MetadataFetchers);
        Assert.Contains("TheMovieDb", series.MetadataFetchers);
        Assert.Contains("HDrezka NFO", options.MetadataSavers);
        Assert.DoesNotContain("Nfo", options.MetadataSavers);
        Assert.Contains("Custom", options.MetadataSavers);
        Assert.True(options.SaveLocalMetadata);
        Assert.True(options.EnableInternetProviders);
        Assert.Equal(new[] { "HDrezka" }, options.TypeOptions.Single(x => x.Type == "Episode").MetadataFetchers);
        Assert.Equal(new[] { "HDrezka" }, options.TypeOptions.Single(x => x.Type == "Season").ImageFetchers);
        SitemapLibraries.ConfigureMetadata(options, CatalogKind.Series);
        Assert.Single(series.MetadataFetchers, x => x == "TheMovieDb");
        Assert.Single(options.MetadataSavers, x => x == "HDrezka NFO");
    }
}
