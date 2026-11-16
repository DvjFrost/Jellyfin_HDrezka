using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class HdrezkaSourceTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public async Task SingleVoiceSeriesIsAvailableOnlyAfterFreeAccessIsConfirmed(int premium, bool expectedFree)
    {
        const string html = "<div class='b-post__title'><h1>Сериал</h1></div><table class='b-post__info'><tr><td><h2>В переводе</h2></td><td>RUDUB</td></tr></table><script>sof.tv.initCDNSeriesEvents(123, 473, 1, 1, false);</script><ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>Серия 1</li></ul>";
        const string episodes = "<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>Серия 1</li></ul>";
        var handler = new Handler(async request => {
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("translator_id=473", body);
            Assert.Contains("action=get_episodes", body);
            var data = new Dictionary<string, object> { ["success"] = true, ["episodes"] = episodes };
            if (premium >= 0) data["premium_content"] = premium;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(data)) };
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://single-series.test/"), http);
        var source = new HdrezkaSource(mirror);
        var listing = await source.DetailAndVoicesAsync("/series/test/123-show.html", CancellationToken.None);
        if (expectedFree)
        {
            var voice = Assert.Single(listing.Voices);
            Assert.Equal("473", voice.Id);
            Assert.Equal("RUDUB", voice.Title);
            Assert.Single(await source.EpisodesFromListingAsync("/series/test/123-show.html", listing, voice, CancellationToken.None));
        }
        else Assert.Empty(listing.Voices);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task SingleFreeSeriesVoiceIsAvailableToDownloads()
    {
        const string html = "<div class='b-post__title'><h1>Сериал</h1></div><script>sof.tv.initCDNSeriesEvents(123, 473, 1, 1, false);</script>";
        const string episodes = "<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>Серия 1</li></ul>";
        var handler = new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? html : System.Text.Json.JsonSerializer.Serialize(new { success = true, premium_content = 0, episodes })) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://single-series-download.test/"), http);
        var listing = await new HdrezkaSource(mirror).VoiceListingAsync("/series/test/123-show.html", CancellationToken.None);
        Assert.Equal("473", Assert.Single(listing.Voices).Id);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task SelectedVoiceReturnsAllAlternativesWithoutExtraRequests()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"url\":\"[720p]https://cdn.example/file.mp4 or https://cdn.example/playlist.m3u8\"}") }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var source = new HdrezkaSource(mirror);
        var voice = new FreeVoice("56", "123", "Free", "0", "0", "0");
        var streams = await source.MovieVoiceAlternativesFromListingAsync("/films/test/123-title.html", voice, CancellationToken.None);
        Assert.Equal(new[] { "/file.mp4", "/playlist.m3u8" }, streams.Select(x => x.Stream.Url.AbsolutePath));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task SelectedEpisodeReturnsAllAlternativesInOneRequest()
    {
        var handler = new Handler(async request => {
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("season=2", body);
            Assert.Contains("episode=3", body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"url\":\"[720p]https://cdn.example/a.mp4 or https://cdn.example/b.m3u8\"}") };
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var voice = new FreeVoice("56", "123", "Free", "0", "0", "0");
        var streams = await new HdrezkaSource(mirror).EpisodeAlternativesFromListingAsync("/series/test/123-title.html", voice, "2", "3", CancellationToken.None);
        Assert.Equal(new[] { "/a.mp4", "/b.m3u8" }, streams.Select(x => x.Stream.Url.AbsolutePath));
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task ActiveVoiceUsesEpisodesEmbeddedInCardWithoutPost()
    {
        const string html = "<div class='b-post__title'><h1>Title</h1></div><script>initCDNSeriesEvents</script><li class='b-translator__item active' data-id='123' data-translator_id='56'>Free</li><ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>One</li></ul>";
        var handler = new Handler(req => { Assert.Equal(HttpMethod.Get, req.Method); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }); });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var source = new HdrezkaSource(mirror);
        var listing = await source.DetailAndVoicesAsync("/series/test/123-title.html", CancellationToken.None);
        var episodes = await source.EpisodesFromListingAsync("/series/test/123-title.html", listing, Assert.Single(listing.Voices), CancellationToken.None);
        Assert.Single(episodes);
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task EpisodeNamesComeFromSameCardWithoutExtraRequests()
    {
        const string html = "<div class='b-post__title'><h1>Title</h1></div><script>initCDNSeriesEvents</script><li class='b-translator__item active' data-id='123' data-translator_id='56'>Free</li><ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>Серия 1</li></ul><div class='b-post__schedule_block'><div class='b-post__schedule_block_title'><div class='title'>1 сезон</div></div><table><tr><td class='td-1'>1 серия</td><td class='td-2'><b>Пилот</b><span>Pilot</span></td></tr></table></div>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://schedule-source.test/"), http);
        var source = new HdrezkaSource(mirror);
        var listing = await source.DetailAndVoicesAsync("/series/test/123-title.html", CancellationToken.None);
        var episodes = await source.EpisodesFromListingAsync("/series/test/123-title.html", listing, Assert.Single(listing.Voices), CancellationToken.None);
        Assert.Equal("Пилот", Assert.Single(episodes).Title);
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task SelectedKnownVoiceFetchesStreamWithoutReloadingCard()
    {
        var handler = new Handler(async request => {
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("translator_id=56", body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"url\":\"[720p]https://cdn.example/720.mp4\"}") };
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var source = new HdrezkaSource(mirror);
        var voice = new FreeVoice("56", "123", "Free", "0", "0", "0");
        Assert.Single(await source.MovieVoiceStreamsFromListingAsync("/films/test/123-title.html", voice, CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task ReusesVoiceListingWithinOneSourceSession()
    {
        const string html = "<div class='b-post__title'><h1>Title</h1></div><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var source = new HdrezkaSource(mirror);
        Assert.Single(await source.VoicesAsync("/films/test/123-title.html", CancellationToken.None));
        Assert.Single(await source.VoicesAsync("/films/test/123-title.html", CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task PreparingTwoVoiceStreamsLoadsTheCardOnlyOnce()
    {
        const string html = "<div class='b-post__title'><h1>Title</h1></div><a class='b-translator__items' data-id='123' data-translator_id='56' title='One'></a><a class='b-translator__items' data-id='123' data-translator_id='57' title='Two'></a>";
        var gets = 0; var posts = 0;
        var handler = new Handler(request => {
            if (request.Method == HttpMethod.Get) gets++; else posts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.Method == HttpMethod.Get ? html : "{\"success\":true,\"url\":\"[720p]https://cdn.example/720.mp4\"}") });
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var source = new HdrezkaSource(mirror);
        Assert.Single(await source.EpisodeStreamsAsync("/series/test/123-title.html", "56", "1", "1", CancellationToken.None));
        Assert.Single(await source.EpisodeStreamsAsync("/series/test/123-title.html", "57", "1", "1", CancellationToken.None));
        Assert.Equal(1, gets);
        Assert.Equal(2, posts);
    }
    [Fact]
    public async Task LoadsDetailAndOnlyFreeVoicesInOneRequest()
    {
        const string html = "<div class='b-post__title'><h1>Title</h1></div><div class='b-post__description_text'>Plot</div><a class='b-prem_translator' data-id='123' data-translator_id='9' title='Paid'></a><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var listing = await new HdrezkaSource(mirror).DetailAndVoicesAsync("/films/a/123-title.html", CancellationToken.None);
        Assert.Equal("Title", listing.Detail.Title);
        Assert.Equal("Free", Assert.Single(listing.Voices).Title);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task EpisodesFromVerifiedListingDoesNotReloadDetailPage()
    {
        var voice = new FreeVoice("56", "123", "Free", "0", "0", "0");
        var listing = new VideoDetailListing(new CatalogDetail("Series", "", true), [voice]);
        var handler = new Handler(request => {
            Assert.Equal(HttpMethod.Post, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"episodes\":\"<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>One</li></ul>\"}") });
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var episodes = await new HdrezkaSource(mirror).EpisodesFromListingAsync("/series/a/123-title.html", listing, voice, CancellationToken.None);
        Assert.Single(episodes);
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task ReadsDetailFromMirrorAndRejectsUnexpectedPage()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<div class='b-post__title'><h1>Title</h1></div><div class='b-post__description_text'>Plot</div>") }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://example.test/"), http);
        var detail = await new HdrezkaSource(mirror).DetailAsync("/films/a/123-title.html", CancellationToken.None);
        Assert.Equal("Title", detail.Title);
        Assert.Equal("Plot", detail.Overview);
    }
    [Fact]
    public async Task MovieStreamsSkipPaidVoicesEvenIfTheyAppearFirst()
    {
        const string html = "<a class='b-translator__items b-prem_translator' data-id='123' data-translator_id='111' title='Paid'></a><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
        var handler = new Handler(async request => {
            if (request.Method == HttpMethod.Get) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) };
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("translator_id=56", body);
            Assert.DoesNotContain("translator_id=111", body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"url\":\"[720p]https://cdn.example/720.m3u8\"}") };
        });
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), http);
        var source = new HdrezkaSource(mirror);
        var streams = await source.MovieStreamsAsync("/films/test/123-test.html", CancellationToken.None);
        var stream = Assert.Single(streams);
        Assert.Equal("Free", stream.Voice);
        Assert.Equal(720, stream.Stream.Quality);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task MovieWithOnlyPaidVoicesNeverRequestsVideo()
    {
        const string html = "<a class='b-translator__items b-prem_translator' data-id='123' data-translator_id='111' title='Paid'></a>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), http);
        var source = new HdrezkaSource(mirror);
        Assert.Empty(await source.MovieStreamsAsync("/films/test/123-test.html", CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task ExplicitPaidVoiceIsRejectedBeforeRequestingStream()
    {
        const string html = "<a class='b-prem_translator' data-id='123' data-translator_id='111' title='Paid'></a>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), http);
        var source = new HdrezkaSource(mirror);
        Assert.Empty(await source.MovieVoiceStreamsAsync("/films/test/123-test.html", "111", CancellationToken.None));
        Assert.Equal(1, handler.Count);
    }
    [Fact]
    public async Task ChoosesHighestFreeQualityAtOrBelow1080()
    {
        const string html = "<a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
        var handler = new Handler(request => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(request.Method == HttpMethod.Get ? html : "{\"success\":true,\"url\":\"[2160p]https://cdn.example/2160.mp4,[720p]https://cdn.example/720.mp4,[1080p]https://cdn.example/1080.mp4\"}") }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), http);
        var best = Assert.Single(await new HdrezkaSource(mirror).MovieBestStreamsAsync("/films/test/123-test.html", CancellationToken.None));
        Assert.Equal(1080, best.Stream.Quality);
    }
    [Fact]
    public async Task MovieVoiceListingUsesFilmTitleAndHidesPaidVoices()
    {
        const string html = "<h1>Фильм &amp; история</h1><a class='b-prem_translator' data-id='123' data-translator_id='111' title='Paid'></a><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>";
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) }));
        using var http = new HttpClient(handler);
        using var mirror = new MirrorClient(new Uri("https://rezka-tv.org/"), http);
        var result = await new HdrezkaSource(mirror).VoiceListingAsync("/films/test/123-test.html", CancellationToken.None);
        Assert.Equal("Фильм & история", result.Title);
        Assert.Equal("Free", Assert.Single(result.Voices).Title);
    }
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Count++; return callback(request); }
    }
}



