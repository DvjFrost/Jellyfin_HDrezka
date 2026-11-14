using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DetailParserTests
{
    [Fact]
    public void PosterAcceptsKnownImageHostAndRejectsUnrelatedLocalAddress()
    {
        const string valid = "<div class='b-post__title'><h1>Title</h1></div><div class='b-sidecover'><img src='https://static.hdrezka.ac/i/a.jpg'></div>";
        Assert.Equal("static.hdrezka.ac", DetailParser.Parse(valid, new Uri("https://mirror.test"))!.Poster!.Host);
        Assert.Null(DetailParser.Parse(valid.Replace("https://static.hdrezka.ac/i/a.jpg", "http://127.0.0.1/admin"), new Uri("https://mirror.test"))!.Poster);
        Assert.Null(DetailParser.Parse(valid.Replace("https://static.hdrezka.ac/i/a.jpg", "https://untrusted.invalid/a.jpg"), new Uri("https://mirror.test"))!.Poster);
    }
    [Fact]
    public void ReadsTitleAndDescriptionWithoutDescriptionHeading()
    {
        const string html = "<div class='b-post__title'><h1>Фильм &amp; герои</h1></div><div class='b-post__description'><div class='b-post__description_title'>Описание</div><div class='b-post__description_text'>Первая часть.<br>Вторая часть.</div></div>";
        var detail = DetailParser.Parse(html);
        Assert.NotNull(detail);
        Assert.Equal("Фильм & герои", detail.Title);
        Assert.Contains("Первая часть.", detail.Overview);
        Assert.Contains("Вторая часть.", detail.Overview);
        Assert.Contains("часть.\nВторая", detail.Overview);
        Assert.DoesNotContain("Описание", detail.Overview);
        Assert.False(detail.HasEpisodes);
    }

    [Fact]
    public void DetectsSeriesStructureForCartoons()
    {
        const string html = "<div class='b-post__title'><h1>Мультсериал</h1></div><ul class='b-simple_seasons__list'></ul>";
        var detail = DetailParser.Parse(html);
        Assert.NotNull(detail);
        Assert.True(detail.HasEpisodes);
    }

    [Fact]
    public void DetectsSeriesPlayerWhenEpisodesAreNotRendered()
    {
        var detail = DetailParser.Parse("<div class='b-post__title'><h1>Title</h1></div><script>player.initCDNSeriesEvents();</script>");
        Assert.NotNull(detail);
        Assert.True(detail.HasEpisodes);
    }

    [Fact]
    public void RejectsPageWithoutVideoTitle()
    {
        Assert.Null(DetailParser.Parse("<html><title>Проверка</title></html>"));
    }
}
