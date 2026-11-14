using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class LibraryVersionMapTests
{
    [Fact]
    public void MapsStablePrimaryAndAlternativeByVoiceId()
    {
        var a = new FreeVoice("2", "123", "First", "0", "0", "0");
        var b = new FreeVoice("3", "123", "Second", "0", "0", "0");
        var record = new LibraryVideoRecord("123", "/films/a/123-title.html", null, null, [a, b]);
        var versions = new[] { new LibraryVersion("primary", "HDrezka-123.strm"), new LibraryVersion("alternate", "HDrezka-123 - voice-3.strm"), new LibraryVersion("stale", "HDrezka-123 - voice-9.strm") };
        var map = LibraryVersionMap.Create(record, versions);
        Assert.Equal("primary", map["2"]);
        Assert.Equal("alternate", map["3"]);
        Assert.Equal(2, map.Count);
    }
}
