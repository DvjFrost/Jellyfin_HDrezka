using System.Xml.Linq;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbMetadataWriterTests
{
    [Fact]
    public async Task KeepsChangesMadeWhileTmdbRequestIsInFlight()
    {
        var path = Path.Combine(Path.GetTempPath(), "hdrezka-race-" + Guid.NewGuid().ToString("N") + ".nfo");
        try
        {
            await File.WriteAllTextAsync(path, "<movie><tmdbid>42</tmdbid><title>Old</title></movie>");
            var handler = new DelayedHandler(); using var http = new HttpClient(handler);
            var pending = TmdbMetadataWriter.EnrichAsync(path, 42, false, new TmdbClient(http, "test"), CancellationToken.None);
            await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await File.WriteAllTextAsync(path, "<movie><tmdbid>42</tmdbid><title>User edit</title><plot>User plot</plot></movie>");
            handler.Release.SetResult(); await pending;
            var result = XDocument.Load(path);
            Assert.Equal("User edit", result.Root!.Element("title")?.Value);
            Assert.Equal("User plot", result.Root.Element("plot")?.Value);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class DelayedHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult(); await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"id\":42,\"title\":\"Remote\",\"overview\":\"Remote plot\"}") };
        }
    }
    [Fact]
    public void RefusesToMixMetadataFromDifferentTitles()
    {
        var doc = XDocument.Parse("<movie><tmdbid>5</tmdbid></movie>");
        var info = new TmdbDetails(42, "Original", "Plot", "", null, [], [], [], null, null);
        Assert.Throws<InvalidOperationException>(() => TmdbMetadataWriter.Merge(doc, info));
    }
    [Fact]
    public void AddsDetailsWithoutReplacingUserMetadataOrOtherRatings()
    {
        var doc = XDocument.Parse("<movie><title>Мой заголовок</title><plot></plot><ratings><rating name='imdb'><value>8.1</value></rating></ratings></movie>");
        var info = new TmdbDetails(42, "Original", "Plot", "Tag", 7.2, ["Drama"], [new("Actor", "Role")], ["Director"], "/a.jpg", "/b.jpg");
        TmdbMetadataWriter.Merge(doc, info);
        Assert.Equal("Мой заголовок", doc.Root!.Element("title")!.Value);
        Assert.Equal("42", doc.Root.Element("tmdbid")?.Value); Assert.Equal("Plot", doc.Root.Element("plot")?.Value);
        Assert.Equal("Original", doc.Root.Element("originaltitle")?.Value);
        Assert.Equal("Role", doc.Root.Element("actor")?.Element("role")?.Value);
        Assert.Equal("8.1", doc.Root.Element("ratings")?.Elements("rating").Single(x => (string?)x.Attribute("name") == "imdb").Element("value")?.Value);
        Assert.Equal("7.2", doc.Root.Element("ratings")?.Elements("rating").Single(x => (string?)x.Attribute("name") == "tmdb").Element("value")?.Value);
    }
}
