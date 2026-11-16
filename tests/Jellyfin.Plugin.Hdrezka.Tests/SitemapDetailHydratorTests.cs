using System.Net;
using System.Xml.Linq;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SitemapDetailHydratorTests
{
    [Fact]
    public async Task UnmatchedItemPreservesUserTitleWhenSiteMetadataIsReloaded()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-site-manual-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "placeholder", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-placeholder-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            const string html = "<div class='b-post__title'><h1>Название сайта</h1></div><div class='b-post__description_text'>Описание</div>";
            using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            await SitemapDetailHydrator.HydrateAsync(root, video, null, new HdrezkaSource(mirror), CancellationToken.None, "Моё название");
            var nfo = XDocument.Load(Path.Combine(root, "films", "HDrezka-123", "movie.nfo"));
            Assert.Equal("Моё название", nfo.Root!.Element("title")?.Value);
            Assert.Equal("Описание", nfo.Root.Element("plot")?.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task SeriesFactsRemainAfterRefreshAndEpisodeStreamCanBeResolved()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-series-facts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "series", 2020, CatalogKind.Series, new Uri("https://mirror.test/series/a/123-series.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            const string html = "<div class='b-post__title'><h1>Сериал</h1></div><script>initCDNSeriesEvents</script><span itemprop='genre'>Драма</span><li class='b-translator__item active' data-id='123' data-translator_id='56'>Free</li><ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>Пилот</li></ul>";
            var calls = 0; using var http = new HttpClient(new Handler(request => { calls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? html : "{\"success\":true,\"url\":\"[720p]https://cdn.example/episode.mp4\"}") }; }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            var source = new HdrezkaSource(mirror);
            await SitemapDetailHydrator.HydrateAsync(root, video, null, source, CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            var dir = Path.Combine(root, "series", "HDrezka-123");
            var nfo = XDocument.Load(Path.Combine(dir, "tvshow.nfo"));
            Assert.Equal("Драма", nfo.Root!.Element("genre")?.Value);
            var episode = Path.Combine(dir, "Season 01", "HDrezka-123 S01E01.strm");
            var record = LibraryRecordLocator.Load(root, episode)!;
            var streams = await source.EpisodeAlternativesFromListingAsync(record.Path, Assert.Single(record.Voices), record.Season!, record.Number!, CancellationToken.None);
            Assert.Equal("/episode.mp4", Assert.Single(streams).Stream.Url.AbsolutePath);
            Assert.Equal(2, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task UnmatchedMovieExtractsFactsFromSamePageAsVoices()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-facts-flow-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "unknown", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-title.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            const string html = "<div class='b-post__title'><h1>Название</h1></div><div class='b-post__description_text'>Описание</div><table class='b-post__info'><tr><td><h2>Жанр</h2>:</td><td><span itemprop='genre'>Драма</span></td></tr><tr><td><h2>Рейтинги</h2>:</td><td><span class='b-post__info_rates imdb'><span class='bold'>8.1</span></span></td></tr><tr><td><span itemprop='actor'><span itemprop='name'>Актёр</span></span></td></tr></table><a data-id='123' data-translator_id='56' title='Free'></a>";
            var calls = 0; using var http = new HttpClient(new Handler(request => { calls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? html : "{\"success\":true,\"url\":\"[720p]https://cdn.example/file.mp4\"}") }; }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            var source = new HdrezkaSource(mirror);
            await SitemapDetailHydrator.HydrateAsync(root, video, null, source, CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            var dir = Path.Combine(root, "films", "HDrezka-123");
            var nfo = XDocument.Load(Path.Combine(dir, "movie.nfo"));
            Assert.Equal(1, calls); Assert.Equal("Драма", nfo.Root!.Element("genre")?.Value);
            Assert.Equal("Актёр", nfo.Root.Element("actor")?.Element("name")?.Value);
            Assert.Equal("8.1", nfo.Root.Element("ratings")?.Element("rating")?.Element("value")?.Value);
            var record = LibraryRecordLocator.Load(root, Path.Combine(dir, "HDrezka-123.strm"))!;
            var voice = Assert.Single(record.Voices);
            var streams = await source.MovieVoiceStreamsFromListingAsync(record.Path, voice, CancellationToken.None);
            Assert.Equal("/file.mp4", Assert.Single(streams).Stream.Url.AbsolutePath);
            Assert.Equal(2, calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(CatalogKind.Movie, "films")]
    [InlineData(CatalogKind.Cartoon, "cartoons")]
    [InlineData(CatalogKind.Anime, "animation")]
    [InlineData(CatalogKind.Show, "show")]
    public async Task UnmatchedMovieLikeItemsUseSiteNameAndPoster(CatalogKind kind, string section)
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-name-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "name translit", 2020, kind, new Uri($"https://mirror.test/{section}/a/123-name-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            const string html = "<div class='b-post__title'><h1>Название с сайта</h1></div><div class='b-post__description_text'>Описание</div><div class='b-sidecover'><img src='https://static.hdrezka.ac/i/poster.jpg'></div>";
            using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            await SitemapDetailHydrator.HydrateAsync(root, video, null, new HdrezkaSource(mirror), CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            var nfo = XDocument.Load(Path.Combine(root, section, "HDrezka-123", "movie.nfo"));
            Assert.Equal("Название с сайта", nfo.Root!.Element("title")!.Value);
            Assert.Equal("Описание", nfo.Root!.Element("plot")!.Value);
            Assert.Equal("https://static.hdrezka.ac/i/poster.jpg", nfo.Root!.Element("thumb")!.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnknownCartoonSeriesIsPreparedWithoutChangingExistingMovie()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-lazy-mixed-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "cartoon", 2020, CatalogKind.Cartoon, new Uri("https://mirror.test/cartoons/a/123-cartoon-2020.html"));
            await SitemapLibraryWriter.WriteAsync(root, video, null, CancellationToken.None);
            var dir = Path.Combine(root, "cartoons", "HDrezka-123");
            var oldNfo = await File.ReadAllTextAsync(Path.Combine(dir, "movie.nfo"));
            const string html = "<div class='b-post__title'><h1>Мультсериал</h1></div><script>initCDNSeriesEvents</script><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
            using var http = new HttpClient(new Handler(req => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.Method == HttpMethod.Get ? html : "{\"success\":true,\"episodes\":\"<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>One</li></ul>\"}") }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            var result = await SitemapDetailHydrator.HydrateAsync(root, video, null, new HdrezkaSource(mirror), CancellationToken.None);
            Assert.NotNull(result.StagedSeriesDirectory);
            Assert.Equal(oldNfo, await File.ReadAllTextAsync(Path.Combine(dir, "movie.nfo")));
            Assert.True(File.Exists(Path.Combine(dir, "HDrezka-123.strm")));
            Assert.False(File.Exists(Path.Combine(dir, "tvshow.nfo")));
            Assert.True(File.Exists(Path.Combine(result.StagedSeriesDirectory, "tvshow.nfo")));
            Assert.True(File.Exists(Path.Combine(result.StagedSeriesDirectory, "Season 01", "HDrezka-123 S01E01.strm")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task SeriesLoadsFreeVoicesAndEpisodesOnlyOnDemand()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-lazy-series-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "show", 2020, CatalogKind.Series, new Uri("https://mirror.test/series/a/123-show-2020.html"));
            var match = new TmdbMatch("123", TmdbMatchStatus.Matched, 33, "Сериал TMDB", "/poster.jpg", "tv");
            await SitemapLibraryWriter.WriteAsync(root, video, match, CancellationToken.None);
            var calls = 0;
            const string html = "<div class='b-post__title'><h1>Site</h1></div><script>initCDNSeriesEvents</script><a class='b-prem_translator' data-id='123' data-translator_id='9' title='Paid'></a><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
            using var http = new HttpClient(new Handler(req => { calls++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(req.Method == HttpMethod.Get ? html : "{\"success\":true,\"episodes\":\"<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>One</li></ul>\"}") }; }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            await SitemapDetailHydrator.HydrateAsync(root, video, null, new HdrezkaSource(mirror), CancellationToken.None);
            Assert.Equal(2, calls);
            var episode = Path.Combine(root, "series", "HDrezka-123", "Season 01", "HDrezka-123 S01E01.strm");
            Assert.True(File.Exists(episode));
            Assert.Equal("Free", Assert.Single(LibraryRecordLocator.Load(root, episode)!.Voices).Title);
            Assert.Equal("Сериал TMDB", XDocument.Load(Path.Combine(root, "series", "HDrezka-123", "tvshow.nfo")).Root!.Element("title")!.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UnmatchedItemGetsSiteMetadataOnlyWhenOpenedAndKeepsItAfterIndexRefresh()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-lazy-unmatched-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "neizvestnyy film", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-test-2020.html"));
            var match = new TmdbMatch("123", TmdbMatchStatus.NoResults, null, null, null);
            await SitemapLibraryWriter.WriteAsync(root, video, match, CancellationToken.None);
            var dir = Path.Combine(root, "films", "HDrezka-123");
            var path = Path.Combine(dir, "movie.nfo");
            Assert.Equal("neizvestnyy film", XDocument.Load(path).Root!.Element("title")!.Value);
            Assert.Null(XDocument.Load(path).Root!.Element("thumb"));
            const string html = "<div class='b-post__title'><h1>Настоящее название</h1></div><div class='b-post__description_text'>Описание</div><div class='b-sidecover'><img src='https://static.hdrezka.ac/i/poster.jpg'></div><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
            using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            await SitemapDetailHydrator.HydrateAsync(root, video, match, new HdrezkaSource(mirror), CancellationToken.None);
            await SitemapLibraryWriter.WriteAsync(root, video, match, CancellationToken.None);
            var nfo = XDocument.Load(path);
            Assert.Equal("Настоящее название", nfo.Root!.Element("title")!.Value);
            Assert.Equal("Описание", nfo.Root!.Element("plot")!.Value);
            Assert.Equal("https://static.hdrezka.ac/i/poster.jpg", nfo.Root!.Element("thumb")!.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MovieUsesSiteOnlyOnDemandAndKeepsTmdbTitleAndPoster()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-lazy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var video = new SitemapVideo("123", "test", 2020, CatalogKind.Movie, new Uri("https://mirror.test/films/a/123-test-2020.html"));
            var match = new TmdbMatch("123", TmdbMatchStatus.Matched, 33, "Имя TMDB", "/poster.jpg", "movie");
            await SitemapLibraryWriter.WriteAsync(root, video, match, CancellationToken.None);
            const string html = "<div class='b-post__title'><h1>Имя сайта</h1></div><div class='b-post__description_text'>Описание</div><a class='b-prem_translator' data-id='123' data-translator_id='9' title='Paid'></a><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
            using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
            using var mirror = new MirrorClient(new Uri("https://mirror.test/"), http);
            await SitemapDetailHydrator.HydrateAsync(root, video, match, new HdrezkaSource(mirror), CancellationToken.None);
            var dir = Path.Combine(root, "films", "HDrezka-123");
            var nfo = XDocument.Load(Path.Combine(dir, "movie.nfo"));
            Assert.Equal("Имя TMDB", nfo.Root!.Element("title")!.Value);
            Assert.Equal("Описание", nfo.Root!.Element("plot")!.Value);
            Assert.Equal("2020", nfo.Root!.Element("year")!.Value);
            Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", nfo.Root!.Element("thumb")!.Value);
            Assert.Equal("Free", Assert.Single(LibraryRecordLocator.Load(root, Path.Combine(dir, "HDrezka-123.strm"))!.Voices).Title);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request));
    }
}
