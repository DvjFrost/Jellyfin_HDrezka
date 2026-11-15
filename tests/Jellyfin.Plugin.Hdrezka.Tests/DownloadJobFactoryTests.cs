using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadJobFactoryTests
{
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
