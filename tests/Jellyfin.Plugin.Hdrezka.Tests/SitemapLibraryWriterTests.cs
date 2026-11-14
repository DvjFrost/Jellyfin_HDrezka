using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SitemapLibraryWriterTests
{
    [Fact]
    public async Task CreatesTransliteratedCardWithoutTmdbAndEnrichesSameFilesLater()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-no-tmdb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "nazvanie filma", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-nazvanie-filma-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            var dir = Path.Combine(root, "films", "HDrezka-123");
            var file = Path.Combine(dir, "HDrezka-123.strm");
            var nfo = Path.Combine(dir, "movie.nfo");
            Assert.True(File.Exists(file));
            var first = System.Xml.Linq.XDocument.Load(nfo);
            Assert.Equal("nazvanie filma", first.Root!.Element("title")!.Value);
            Assert.Null(first.Root.Element("thumb"));

            await SitemapLibraryWriter.WriteAsync(root, video, new("123", TmdbMatchStatus.Matched, 45, "Название фильма", "/poster.jpg", "movie"), CancellationToken.None);
            var updated = System.Xml.Linq.XDocument.Load(nfo);
            Assert.True(File.Exists(file));
            Assert.Equal("Название фильма", updated.Root!.Element("title")!.Value);
            Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", updated.Root.Element("thumb")!.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WritesMovieAndEmptySeriesWithoutOpeningSiteCards()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-sitemap-library-" + Guid.NewGuid().ToString("N"));
        try
        {
            var movie = new SitemapVideo("123", "test", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-test-2020.html"));
            var series = new SitemapVideo("456", "show", 2021, CatalogKind.Series, new Uri("https://mirror.test/series/a/456-show-2021.html"));
            var voice = new FreeVoice("1", "123", "Free", "0", "0", "0");
            await MovieLibraryWriter.WriteAsync(Path.Combine(root, "films"), new CatalogEntry("Old", movie.Url, null, "2020", CatalogKind.Movie), new CatalogDetail("Old", "Old description", false), [voice], CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, movie, new("123", TmdbMatchStatus.Matched, 33, "Тест", "/poster.jpg", "movie"), CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, series, new("456", TmdbMatchStatus.Matched, 44, "Сериал", "/series.jpg", "tv"), CancellationToken.None);
            Assert.True(File.Exists(Path.Combine(root, "films", "HDrezka-123", "HDrezka-123.strm")));
            Assert.Equal("Тест", System.Xml.Linq.XDocument.Load(Path.Combine(root, "films", "HDrezka-123", "movie.nfo")).Root!.Element("title")!.Value);
            Assert.Equal("Old description", System.Xml.Linq.XDocument.Load(Path.Combine(root, "films", "HDrezka-123", "movie.nfo")).Root!.Element("plot")!.Value);
            Assert.Single(LibraryRecordLocator.Load(root, Path.Combine(root, "films", "HDrezka-123", "HDrezka-123.strm"))!.Voices);
            await SitemapLibraryWriter.WriteAsync(root, movie, null, CancellationToken.None);
            Assert.Equal("Тест", System.Xml.Linq.XDocument.Load(Path.Combine(root, "films", "HDrezka-123", "movie.nfo")).Root!.Element("title")!.Value);
            Assert.NotNull(System.Xml.Linq.XDocument.Load(Path.Combine(root, "films", "HDrezka-123", "movie.nfo")).Root!.Element("thumb"));
            Assert.Equal("Сериал", System.Xml.Linq.XDocument.Load(Path.Combine(root, "series", "HDrezka-456", "tvshow.nfo")).Root!.Element("title")!.Value);
            Assert.True(File.Exists(Path.Combine(root, "series", "HDrezka-456", "hdrezka-series.json")));
            var missing = new SitemapVideo("789", "dnevnik gornichnoy", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/789-dnevnik-gornichnoy-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, missing, null, CancellationToken.None);
            Assert.Equal("dnevnik gornichnoy", System.Xml.Linq.XDocument.Load(Path.Combine(root, "films", "HDrezka-789", "movie.nfo")).Root!.Element("title")!.Value);
            var anime = new SitemapVideo("999", "anime", 2020, CatalogKind.Anime, new Uri("https://mirror.test/animation/a/999-anime-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, anime, new("999", TmdbMatchStatus.Matched, 55, "Аниме", null, "tv"), CancellationToken.None);
            var animeDir = Path.Combine(root, "animation", "HDrezka-999");
            Assert.True(File.Exists(Path.Combine(animeDir, "tvshow.nfo")));
            Assert.Equal("999", LibrarySeriesLocator.Load(root, animeDir)!.Id);
            Assert.False(Directory.Exists(Path.Combine(root, "cartoons", "HDrezka-999")));
            var show = new SitemapVideo("888", "show", 2020, CatalogKind.Show, new Uri("https://mirror.test/show/humor/888-show-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, show, new("888", TmdbMatchStatus.Matched, 56, "Шоу", null, "tv"), CancellationToken.None);
            Assert.Equal("888", LibrarySeriesLocator.Load(root, Path.Combine(root, "show", "HDrezka-888"))!.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task IndexRefreshDoesNotCreateConflictingMediaTypesForExistingMixedItem()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-mixed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "cartoon", 2020, CatalogKind.Cartoon, new Uri("https://mirror.test/cartoons/a/123-cartoon-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, video, new("123", TmdbMatchStatus.Matched, 12, "Название", null, "tv"), CancellationToken.None);
            var dir = Path.Combine(root, "cartoons", "HDrezka-123");
            Assert.True(File.Exists(Path.Combine(dir, "movie.nfo")));
            Assert.False(File.Exists(Path.Combine(dir, "tvshow.nfo")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
