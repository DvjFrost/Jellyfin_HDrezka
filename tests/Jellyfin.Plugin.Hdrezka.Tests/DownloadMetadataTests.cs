using System.Xml.Linq;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadMetadataTests
{
    [Fact]
    public async Task WritesEpisodeTitleWithoutFilePrefixAndNeverOverwritesExistingNfo()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-nfo-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "S02E14 Секс убивает.mp4");
            var job = new DownloadJob(Guid.NewGuid(), "/series/a/123-test.html", "1", 480, "library", "episode.mp4", 2, 14, DownloadJobStatus.Queued, null, DisplayTitle: "Доктор Хаус", EpisodeTitle: "Секс убивает");
            await DownloadMetadata.WriteAsync(destination, job, CancellationToken.None);
            var nfo = Path.ChangeExtension(destination, ".nfo");
            var xml = XDocument.Load(nfo);
            Assert.Equal("Секс убивает", xml.Root?.Element("title")?.Value);
            Assert.Equal("2", xml.Root?.Element("season")?.Value);
            Assert.Equal("14", xml.Root?.Element("episode")?.Value);
            await File.WriteAllTextAsync(nfo, "user metadata");
            await DownloadMetadata.WriteAsync(destination, job, CancellationToken.None);
            Assert.Equal("user metadata", await File.ReadAllTextAsync(nfo));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WritesOriginalMovieTitleAndYear()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-nfo-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "Белый призрак (1988).mp4");
            var job = new DownloadJob(Guid.NewGuid(), "/films/a/123-test.html", "1", 720, "library", "movie.mp4", null, null, DownloadJobStatus.Queued, null, DisplayTitle: "Белый призрак", Year: 1988);
            await DownloadMetadata.WriteAsync(destination, job, CancellationToken.None);
            var xml = XDocument.Load(Path.ChangeExtension(destination, ".nfo"));
            Assert.Equal("Белый призрак", xml.Root?.Element("title")?.Value);
            Assert.Equal("1988", xml.Root?.Element("year")?.Value);
        }
        finally { Directory.Delete(root, true); }
    }
}
