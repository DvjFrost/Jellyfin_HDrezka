using System.Net;
using System.Xml.Linq;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbMetadataEnricherTests
{
    [Fact]
    public async Task MigratesConfirmedMatchOnceAndKeepsPlaybackRecordUntouched()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrezka-enrich-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var nfo = Path.Combine(dir, "movie.nfo"); var record = Path.Combine(dir, "hdrezka.json");
            await File.WriteAllTextAsync(nfo, "<movie><title>Name</title><plot></plot></movie>");
            await File.WriteAllTextAsync(record, "{\"Id\":\"123\",\"Path\":\"/films/a/123-x.html\"}");
            var handler = new Handler(); using var http = new HttpClient(handler); var client = new TmdbClient(http, "test");
            Assert.True(await TmdbMetadataEnricher.ProcessAsync(nfo, 42, false, true, false, client, CancellationToken.None));
            Assert.Equal("42", await File.ReadAllTextAsync(Path.Combine(dir, "hdrezka-tmdb-refresh-pending.txt")));
            Assert.False(await TmdbMetadataEnricher.ProcessAsync(nfo, 42, false, true, false, client, CancellationToken.None));
            Assert.Equal(1, handler.Calls); Assert.Equal("42", XDocument.Load(nfo).Root!.Element("tmdbid")?.Value);
            Assert.Contains("/films/a/123-x.html", await File.ReadAllTextAsync(record));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DoesNotReplaceExistingOrLockedIdentity()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrezka-enrich-lock-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var nfo = Path.Combine(dir, "movie.nfo");
            await File.WriteAllTextAsync(nfo, "<movie><tmdbid>99</tmdbid></movie>");
            var handler = new Handler(); using var http = new HttpClient(handler); var client = new TmdbClient(http, "test");
            Assert.False(await TmdbMetadataEnricher.ProcessAsync(nfo, 42, false, true, false, client, CancellationToken.None));
            Assert.False(await TmdbMetadataEnricher.ProcessAsync(nfo, 42, false, true, true, client, CancellationToken.None));
            Assert.Equal(0, handler.Calls);
        }
        finally { Directory.Delete(dir, true); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":42,\"overview\":\"Plot\"}") }); }
    }
}
