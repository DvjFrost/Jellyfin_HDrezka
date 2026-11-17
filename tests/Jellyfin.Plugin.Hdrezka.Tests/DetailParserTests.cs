using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DetailParserTests
{
    [Fact]
    public void ParsesOriginalTitleReleaseDateImdbAndAgeFromSitePage()
    {
        const string html = """
            <div class='b-post__title'><h1>18 лет спустя</h1></div>
            <div class='b-post__origtitle'>18 ans après / 18 Years Later</div>
            <table class='b-post__info'>
              <tr><td><h2>Рейтинги</h2>:</td><td><span class='b-post__info_rates imdb'><a href='/help/aHR0cHMlM0ElMkYlMkZ3d3cuaW1kYi5jb20lMkZ0aXRsZSUyRnR0MDI3MTMzNyUyRg==/'>IMDb: 4.6</a></span></td></tr>
              <tr><td><h2>Дата выхода</h2>:</td><td>5 февраля <a>2003 года</a></td></tr>
              <tr><td><h2>Возраст</h2>:</td><td>12+ взрослые темы и понятия, но темы не раскрыты</td></tr>
            </table>
            """;
        var detail = DetailParser.Parse(html, new Uri("https://mirror.test"))!;
        Assert.Equal("18 ans après", detail.OriginalTitle);
        Assert.Equal("2003-02-05", detail.ReleaseDate);
        Assert.Equal("tt0271337", detail.ImdbId);
        Assert.Equal("12+", detail.AgeRating);
    }

    [Fact]
    public void RejectsUntrustedImdbLinksAndIncompleteOrInvalidDates()
    {
        const string html = """
            <div class='b-post__title'><h1>Test</h1></div>
            <table class='b-post__info'>
              <tr><td><h2>Рейтинги</h2></td><td><span class='b-post__info_rates imdb'><a href='https://evil.example/title/tt1234567/'>IMDb</a></span></td></tr>
              <tr><td><h2>Дата выхода</h2></td><td>31 февраля 2003 года</td></tr>
              <tr><td><h2>Возраст</h2></td><td>72+</td></tr>
            </table>
            """;
        var detail = DetailParser.Parse(html, new Uri("https://mirror.test"))!;
        Assert.Null(detail.ImdbId);
        Assert.Null(detail.ReleaseDate);
        Assert.Null(detail.AgeRating);
    }

    [Fact]
    public void KeepsSlashWhenItIsPartOfOriginalTitle()
    {
        var detail = DetailParser.Parse("<div class='b-post__title'><h1>Без лица</h1></div><div class='b-post__origtitle'>Face/Off / Face Off</div>")!;
        Assert.Equal("Face/Off", detail.OriginalTitle);
    }

    [Theory]
    [InlineData("https://www.imdb.com/title/tt0412142/", "tt0412142")]
    [InlineData("https://imdb.com/title/tt0412142?ref_=x", "tt0412142")]
    [InlineData("https://imdb.com.evil.example/title/tt0412142/", null)]
    [InlineData("https://www.imdb.com@evil.example/title/tt0412142/", null)]
    [InlineData("/help/not-valid-base64/", null)]
    public void AcceptsOnlyActualImdbIdentifiers(string href, string? expected)
    {
        var html = "<div class='b-post__title'><h1>Test</h1></div><span class='b-post__info_rates imdb'><a href='" + href + "'>IMDb</a></span>";
        Assert.Equal(expected, DetailParser.Parse(html, new Uri("https://mirror.test"))!.ImdbId);
    }
    [Fact]
    public void ExtractsStructuredCreditsGenresAndImdbRating()
    {
        const string html = "<div class='b-post__title'><h1>Тест</h1></div><table class='b-post__info'><tr><td><h2>Жанр</h2>:</td><td><a><span itemprop='genre'>Драма</span></a>, <a><span itemprop='genre'>Боевик</span></a></td></tr><tr><td><h2>Слоган</h2>:</td><td>Всё получится</td></tr><tr><td><h2>В ролях</h2>:</td><td><span itemprop='actor'><span itemprop='name'>Актёр Один</span></span></td></tr><tr><td><h2>Рейтинги</h2>:</td><td><span class='b-post__info_rates imdb'>IMDb: <span class='bold'>8.1</span></span></td></tr></table>";
        var detail = DetailParser.Parse(html)!;
        Assert.Equal(new[] { "Драма", "Боевик" }, detail.Genres);
        Assert.Equal(new[] { "Актёр Один" }, detail.Actors);
        Assert.Equal("Всё получится", detail.Tagline);
        Assert.Equal(8.1, detail.ImdbRating);
    }
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
