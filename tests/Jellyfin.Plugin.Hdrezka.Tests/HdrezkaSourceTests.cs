using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class HdrezkaSourceTests
{
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



