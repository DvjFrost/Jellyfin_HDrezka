using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MovieLibraryWriterTests
{
    [Fact]
    public async Task LockedMetadataAndExistingImdbUniqueIdAreNotReplaced()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-locked-site-fields-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Name", new Uri("https://example.test/films/a/123-title.html"), null, "2003", CatalogKind.Movie);
            var dir = Path.Combine(root, "HDrezka-123"); Directory.CreateDirectory(dir);
            var nfo = Path.Combine(dir, "movie.nfo");
            await File.WriteAllTextAsync(nfo, "<movie><title>Name</title><lockdata>true</lockdata><uniqueid type='imdb'>tt1111111</uniqueid></movie>");
            var detail = new CatalogDetail("Name", "", false) { OriginalTitle = "Source", ReleaseDate = "2003-02-05", ImdbId = "tt0271337", AgeRating = "12+" };
            await MovieLibraryWriter.WriteAsync(root, entry, detail, [], CancellationToken.None);
            var doc = System.Xml.Linq.XDocument.Load(nfo);
            Assert.Null(doc.Root!.Element("originaltitle"));
            Assert.Null(doc.Root.Element("premiered"));
            Assert.Null(doc.Root.Element("mpaa"));
            Assert.Null(doc.Root.Element("imdbid"));
            Assert.Equal("tt1111111", doc.Root.Element("uniqueid")?.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RespectsIndividualNameAndAgeLocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-field-locks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Name", new Uri("https://example.test/films/a/123-title.html"), null, "2003", CatalogKind.Movie);
            var dir = Path.Combine(root, "HDrezka-123"); Directory.CreateDirectory(dir);
            var nfo = Path.Combine(dir, "movie.nfo");
            await File.WriteAllTextAsync(nfo, "<movie><title>Name</title><lockedfields>Name|OfficialRating</lockedfields></movie>");
            await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Name", "", false) { OriginalTitle = "Source", ReleaseDate = "2003-02-05", AgeRating = "12+" }, [], CancellationToken.None);
            var doc = System.Xml.Linq.XDocument.Load(nfo);
            Assert.Null(doc.Root!.Element("originaltitle"));
            Assert.Null(doc.Root.Element("mpaa"));
            Assert.Equal("2003-02-05", doc.Root.Element("premiered")?.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FillsMissingSiteFactsWithoutReplacingExistingValues(bool series)
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-site-fields-" + Guid.NewGuid().ToString("N"));
        try
        {
            var entry = new CatalogEntry("Name", new Uri("https://example.test/films/a/123-title.html"), null, "2003", series ? CatalogKind.Series : CatalogKind.Movie);
            var dir = Path.Combine(root, "HDrezka-123"); Directory.CreateDirectory(dir);
            var nfo = Path.Combine(dir, series ? "tvshow.nfo" : "movie.nfo");
            var tag = series ? "tvshow" : "movie";
            await File.WriteAllTextAsync(nfo, $"<{tag}><title>Name</title><originaltitle>Ручное оригинальное</originaltitle><imdbid>tt1111111</imdbid></{tag}>");
            var detail = new CatalogDetail("Name", "Plot", series) { OriginalTitle = "Source", ReleaseDate = "2003-02-05", ImdbId = "tt0271337", AgeRating = "12+" };
            if (series) await SeriesLibraryWriter.WriteAsync(root, entry, detail, [], CancellationToken.None);
            else await MovieLibraryWriter.WriteAsync(root, entry, detail, [], CancellationToken.None);
            var doc = System.Xml.Linq.XDocument.Load(nfo);
            Assert.Equal("Ручное оригинальное", doc.Root!.Element("originaltitle")?.Value);
            Assert.Equal("tt1111111", doc.Root.Element("imdbid")?.Value);
            Assert.Equal("2003-02-05", doc.Root.Element("premiered")?.Value);
            Assert.Equal("12+", doc.Root.Element("mpaa")?.Value);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

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
