using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SeriesLibraryWriterTests
{
    [Fact]
    public async Task WritesEpisodesWithStableNamesAndOnlyTheirAvailableVoices()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-series-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Series", new Uri("https://example.test/series/a/123-title.html"), null, "", CatalogKind.Series);
            var detail = new CatalogDetail("Series", "Plot", true);
            var first = new FreeVoice("2", "123", "First", "0", "0", "0");
            var second = new FreeVoice("3", "123", "Second", "0", "0", "0");
            var voices = new[] { new SeriesVoiceEpisodes(first, [new Episode("1", "1", "One"), new Episode("1", "2", "Two")]), new SeriesVoiceEpisodes(second, [new Episode("1", "1", "One")]) };
            await SeriesLibraryWriter.WriteAsync(root, entry, detail, voices, CancellationToken.None);
            var season = Path.Combine(root, "HDrezka-123", "Season 01");
            Assert.True(File.Exists(Path.Combine(season, "HDrezka-123 S01E01.strm")));
            Assert.True(File.Exists(Path.Combine(season, "HDrezka-123 S01E01 - voice-3.strm")));
            Assert.True(File.Exists(Path.Combine(season, "HDrezka-123 S01E02.strm")));
            Assert.False(File.Exists(Path.Combine(season, "HDrezka-123 S01E02 - voice-3.strm")));
            Assert.Equal("Series", System.Xml.Linq.XDocument.Load(Path.Combine(root, "HDrezka-123", "tvshow.nfo")).Root!.Element("title")!.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
