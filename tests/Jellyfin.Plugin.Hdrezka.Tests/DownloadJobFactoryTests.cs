using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadJobFactoryTests
{
    [Fact]
    public void SeriesUsingMoviePlayerKeepsStreamKindForDownload()
    {
        var voice = new FreeVoice("56", "123", "Озвучка", "0", "0", "0");
        var record = new LibraryVideoRecord("123", "/series/horror/123-title.html", "1", "1", [voice], UseMoviePlayer: true);
        var job = DownloadJobFactory.Create(record, "Сериал", 2004, "Полная версия", "56", 720, "series");
        Assert.True(job.UseMoviePlayer);
        Assert.False(job.MoviePart);
        Assert.Equal(1, job.Season);
        Assert.Equal(1, job.Episode);
    }

    [Fact]
    public void MultipartMoviePartsHaveDistinctStackableNames()
    {
        Assert.Equal("Ирония судьбы (1975) - pt1.mp4", DownloadPaths.MoviePart("Ирония судьбы", 1975, 1));
        Assert.Equal("Ирония судьбы (1975) - pt2.mp4", DownloadPaths.MoviePart("Ирония судьбы", 1975, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownloadPaths.MoviePart("Фильм", 2014, 0));
    }

    [Fact]
    public void MultipartMovieJobUsesEpisodeStreamButMovieDestination()
    {
        var voice = new FreeVoice("56", "123", "Дубляж", "0", "0", "0");
        var record = new LibraryVideoRecord("123", "/films/drama/123-film.html", "1", "2", [voice]);
        var job = DownloadJobFactory.CreateMoviePart(record, "Фильм", 2014, 2, "56", 1080, "movies");
        Assert.Equal("Фильм (2014) - pt2.mp4", job.RelativePath);
        Assert.Equal(1, job.Season);
        Assert.Equal(2, job.Episode);
        Assert.Equal("movies", job.LibraryId);
        Assert.True(job.MoviePart);
        Assert.Throws<ArgumentException>(() => DownloadJobFactory.CreateMoviePart(record, "Фильм", 2014, 2, "99", 1080, "movies"));
    }

    [Fact]
    public void MultipartMovieOffersOnlyVoicesAvailableForAllParts()
    {
        var common = new FreeVoice("1", "123", "Общая", "0", "0", "0");
        var firstOnly = new FreeVoice("2", "123", "Только первая", "0", "0", "0");
        var parts = new[] { new MultipartMoviePart("1", "1", "Часть 1", [common, firstOnly]), new MultipartMoviePart("1", "2", "Часть 2", [common]) };
        Assert.Equal("1", Assert.Single(MultipartMovieWriter.CommonVoices(parts)).Id);
        Assert.Empty(MultipartMovieWriter.CommonVoices([]));
    }

    [Fact]
    public void BuildsOnlyForListedFreeVoiceAndGeneratesName()
    {
        var voice = new FreeVoice("56", "123", "Дубляж", "0", "0", "0");
        var record = new LibraryVideoRecord("123", "/series/drama/123-house.html", "1", "2", [voice]);
        var job = DownloadJobFactory.Create(record, "Доктор Хаус", null, "Название серии", "56", 1080, "destination");
        Assert.Equal(Path.Combine("Доктор Хаус (Дубляж)", "Сезон 01", "S01E02 Название серии.mp4"), job.RelativePath);
        Assert.Equal(DownloadJobStatus.Queued, job.Status);
        Assert.Throws<ArgumentException>(() => DownloadJobFactory.Create(record, "Доктор Хаус", null, "Название", "99", 1080, "destination"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownloadJobFactory.Create(record, "Доктор Хаус", null, "Название", "56", 2160, "destination"));
    }
}
