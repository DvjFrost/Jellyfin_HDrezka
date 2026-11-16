using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MovieLibraryWriterTests
{
    [Fact]
    public async Task WritesSiteFactsAndKeepsImdbRatingIdentified()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-facts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Name", new Uri("https://example.test/films/a/123-title.html"), null, "2020", CatalogKind.Movie);
            var detail = new CatalogDetail("Name", "Plot", false) { Genres = ["Драма"], Actors = ["Актёр"], Tagline = "Слоган", ImdbRating = 8.1 };
            await MovieLibraryWriter.WriteAsync(root, entry, detail, [], CancellationToken.None);
            var doc = System.Xml.Linq.XDocument.Load(Path.Combine(root, "HDrezka-123", "movie.nfo"));
            Assert.Equal("Драма", doc.Root!.Element("genre")?.Value);
            Assert.Equal("Актёр", doc.Root.Element("actor")?.Element("name")?.Value);
            Assert.Equal("Слоган", doc.Root.Element("tagline")?.Value);
            Assert.Equal("imdb", doc.Root.Element("ratings")?.Element("rating")?.Attribute("name")?.Value);
            Assert.Equal("8.1", doc.Root.Element("ratings")?.Element("rating")?.Element("value")?.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshPreservesEnrichedNfoAndPlaybackRecord(bool series)
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-merge-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Name", new Uri("https://example.test/films/a/123-title.html"), null, "2020", series ? CatalogKind.Series : CatalogKind.Movie);
            var dir = Path.Combine(root, "HDrezka-123"); Directory.CreateDirectory(dir);
            var nfo = Path.Combine(dir, series ? "tvshow.nfo" : "movie.nfo");
            await File.WriteAllTextAsync(nfo, $"<{(series ? "tvshow" : "movie")}><title>Name</title><plot>Existing plot</plot><tmdbid>12345</tmdbid><genre>Drama</genre><actor><name>Actor</name></actor><ratings><rating name='imdb'><value>8.1</value></rating></ratings></{(series ? "tvshow" : "movie")}>");
            if (series) await SeriesLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Name", "", true), [], CancellationToken.None);
            else await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Name", "", false), [new FreeVoice("1", "123", "Voice", "0", "0", "0")], CancellationToken.None);
            var doc = System.Xml.Linq.XDocument.Load(nfo);
            Assert.Equal("12345", doc.Root!.Element("tmdbid")?.Value);
            Assert.Equal("Drama", doc.Root.Element("genre")?.Value);
            Assert.Equal("Actor", doc.Root.Element("actor")?.Element("name")?.Value);
            Assert.Equal("Existing plot", doc.Root.Element("plot")?.Value);
            Assert.Equal("8.1", doc.Root.Element("ratings")?.Element("rating")?.Element("value")?.Value);
            if (!series) Assert.Equal("1", Assert.Single(LibraryRecordLocator.Load(root, Path.Combine(dir, "HDrezka-123.strm"))!.Voices).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
