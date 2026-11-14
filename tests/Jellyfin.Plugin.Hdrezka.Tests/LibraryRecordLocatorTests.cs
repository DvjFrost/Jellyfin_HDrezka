using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class LibraryRecordLocatorTests
{
    [Fact]
    public async Task FindsMovieRecordOnlyInsideOwnedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-locator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Title", new Uri("https://example.test/films/a/123-title.html"), null, "", CatalogKind.Movie);
            var voice = new FreeVoice("56", "123", "Free", "0", "0", "0");
            await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Title", "Plot", false), [voice], CancellationToken.None);
            var path = Path.Combine(root, "HDrezka-123", "HDrezka-123.strm");
            var record = LibraryRecordLocator.Load(root, path);
            Assert.NotNull(record);
            Assert.Equal("/films/a/123-title.html", record.Path);
            Assert.Single(record.Voices);
            Assert.Null(LibraryRecordLocator.Load(Path.Combine(root, "other"), path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FindsEpisodeRecordForAlternativeVoiceFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-locator-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Series", new Uri("https://example.test/series/a/123-title.html"), null, "", CatalogKind.Series);
            var a = new FreeVoice("2", "123", "A", "0", "0", "0"); var b = new FreeVoice("3", "123", "B", "0", "0", "0");
            await SeriesLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Series", "", true), [new(a, [new("1", "1", "One")]), new(b, [new("1", "1", "One")])], CancellationToken.None);
            var path = Path.Combine(root, "HDrezka-123", "Season 01", "HDrezka-123 S01E01 - voice-3.strm");
            var record = LibraryRecordLocator.Load(root, path);
            Assert.NotNull(record);
            Assert.Equal("1", record.Season);
            Assert.Equal("1", record.Number);
            Assert.Equal(2, record.Voices.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
