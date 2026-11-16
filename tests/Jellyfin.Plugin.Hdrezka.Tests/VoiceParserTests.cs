using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class VoiceParserTests
{
    [Fact]
    public void DoesNotInferSingleSeriesVoiceWhenPaidListExistsOrIdDiffers()
    {
        const string paid = "<li class='b-prem_translator' data-id='123' data-translator_id='473'>Paid</li><script>sof.tv.initCDNSeriesEvents(123, 473, 1, 1, false);</script>";
        const string other = "<script>sof.tv.initCDNSeriesEvents(999, 473, 1, 1, false);</script>";
        Assert.Null(VoiceParser.ParseSingleSeriesCandidate(paid, "123"));
        Assert.Null(VoiceParser.ParseSingleSeriesCandidate(other, "123"));
    }
    [Fact]
    public void KeepsOnlyFreeVoicesInSiteOrder()
    {
        const string html = "<a class='b-translator__items b-prem_translator' data-id='88809' data-translator_id='111' title='Paid'></a><a class='b-translator__items' data-id='88809' data-translator_id='56' data-camrip='0' data-ads='1' data-director='0' title='Free'></a>";
        var free = Assert.Single(VoiceParser.ParseFree(html));
        Assert.Equal("56", free.Id);
        Assert.Equal("88809", free.ContentId);
        Assert.Equal("Free", free.Title);
        Assert.Equal("1", free.IsAds);
    }
    [Fact]
    public void DoesNotInventAnOptionWhenOnlyPremiumVoicesExist()
    {
        const string html = "<li class='b-translator__item b-prem_translator' data-id='88809' data-translator_id='111'>Paid</li>";
        Assert.Empty(VoiceParser.ParseFree(html));
    }
    [Fact]
    public void RejectsInvalidOrExplicitlyPaidValues()
    {
        const string html = "<a class='b-translator__items' data-translator_id='56' data-premium='1'>Paid</a><a class='b-translator__items' data-translator_id='abc'>Invalid</a>";
        Assert.Empty(VoiceParser.ParseFree(html));
    }

    [Fact]
    public void ReadsSingleFreeMovieVoiceFromPlayerInitialization()
    {
        const string html = "<script>sof.tv.initCDNMoviesEvents(87088, 59, 0, 0, 0, 'mirror.test', false, false, {\"id\":\"cdnplayer\",\"streams\":\"[360p]https://cdn.test/a\"});</script>";
        var voice = Assert.Single(VoiceParser.ParseFree(html, "87088"));
        Assert.Equal("59", voice.Id);
        Assert.Equal("Многоголосый закадровый", voice.Title);
        Assert.Equal("87088", voice.ContentId);
        Assert.Empty(VoiceParser.ParseFree(html, "87105"));
    }

    [Fact]
    public void ReadsSingleVoiceNameFromMovieInfo()
    {
        const string html = "<table class='b-post__info'><tr><td><h2>В переводе</h2>:</td><td>Музовоз</td></tr></table><script>sof.tv.initCDNMoviesEvents(92655, 354, 0, 0, 0, 'mirror.test', false, false, {\"id\":\"cdnplayer\",\"streams\":\"[360p]https://cdn.test/a\"});</script>";
        Assert.Equal("Музовоз", Assert.Single(VoiceParser.ParseFree(html, "92655")).Title);
    }

    [Fact]
    public void DoesNotAssignAmbiguousMultipleTranslationsToOneVoice()
    {
        const string html = "<table class='b-post__info'><tr><td><h2>В переводе</h2>:</td><td>Музовоз, Дубляж</td></tr></table><script>sof.tv.initCDNMoviesEvents(92655, 354, 0, 0, 0, 'mirror.test', false, false, {\"id\":\"cdnplayer\",\"streams\":\"[360p]https://cdn.test/a\"});</script>";
        Assert.Equal("Доступная озвучка", Assert.Single(VoiceParser.ParseFree(html, "92655")).Title);
    }

    [Fact]
    public void NeverUsesPlayerFallbackForPremiumOnlyOrEmptyStreams()
    {
        const string call = "<script>sof.tv.initCDNMoviesEvents(123, 59, 0, 0, 0, 'mirror.test', false, false, {\"id\":\"cdnplayer\",\"streams\":\"[360p]https://cdn.test/a\"});</script>";
        Assert.Empty(VoiceParser.ParseFree("<li class='b-prem_translator' data-translator_id='59'>Paid</li>" + call, "123"));
        Assert.Empty(VoiceParser.ParseFree(call.Replace("[360p]https://cdn.test/a", ""), "123"));
        Assert.Empty(VoiceParser.ParseFree(call.Replace("false, false", "true, false"), "123"));
    }
}
