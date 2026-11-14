using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MovieLibraryWriterTests
{
    [Fact]
    public async Task WritesStablePrimaryAndEscapesMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-writer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("A & B", new Uri("https://example.test/films/a/123-title.html"), null, "2023, США, Драма", CatalogKind.Movie);
            await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("A & B", "<description>", false), [], CancellationToken.None);
            var dir = Path.Combine(root, "HDrezka-123");
            Assert.True(File.Exists(Path.Combine(dir, "HDrezka-123.strm")));
            var nfo = System.Xml.Linq.XDocument.Load(Path.Combine(dir, "movie.nfo"));
            Assert.Equal("A & B", nfo.Root!.Element("title")!.Value);
            Assert.Equal("<description>", nfo.Root.Element("plot")!.Value);
            Assert.Equal("2023", nfo.Root.Element("year")!.Value);
            Assert.Null(nfo.Root.Element("lockdata"));
            Assert.True(File.Exists(Path.Combine(dir, "hdrezka.json")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task UpdatesKeepPrimaryAndDoNotDeleteOldVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-writer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Title", new Uri("https://example.test/films/a/123-title.html"), null, "", CatalogKind.Movie);
            var detail = new CatalogDetail("Title", "Description", false);
            FreeVoice Voice(string id) => new(id, "123", "Voice " + id, "0", "0", "0");
            await MovieLibraryWriter.WriteAsync(root, entry, detail, [Voice("2"), Voice("3")], CancellationToken.None);
            await MovieLibraryWriter.WriteAsync(root, entry, detail, [Voice("2"), Voice("4")], CancellationToken.None);
            var dir = Path.Combine(root, "HDrezka-123");
            Assert.True(File.Exists(Path.Combine(dir, "HDrezka-123.strm")));
            Assert.True(File.Exists(Path.Combine(dir, "HDrezka-123 - voice-3.strm")));
            Assert.True(File.Exists(Path.Combine(dir, "HDrezka-123 - voice-4.strm")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
