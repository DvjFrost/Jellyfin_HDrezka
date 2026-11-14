using System.Text;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class StreamParserTests
{
    [Fact]
    public void ParsesPlainAndEncodedStreamsWithQualityAndAlternativeUrls()
    {
        const string plain = "[360p]https://cdn.example/360.m3u8,[720p]https://cdn.example/720.m3u8 or https://alt.example/720.m3u8,[1080p]https://cdn.example/1080.m3u8";
        var encoded = "#h" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        var list = StreamParser.Parse(encoded);
        Assert.Equal(new[] { 360, 720, 1080 }, list.Select(x => x.Quality));
        Assert.Equal("https://alt.example/720.m3u8", list[1].Url.AbsoluteUri);
        Assert.Equal(1080, StreamParser.Parse(plain).Last().Quality);
    }
    [Fact]
    public void RemovesNoiseAndRejectsMalformedOrUnsafeUrls()
    {
        var plain = "[720p]https://cdn.example/720.m3u8";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        encoded = encoded.Insert(5, "//_//1234567890123456");
        Assert.Single(StreamParser.Parse("#h" + encoded));
        Assert.Empty(StreamParser.Parse("#h!!!"));
        Assert.Empty(StreamParser.Parse("[720p]file:///etc/passwd,[360p]javascript:alert(1)"));
    }
    [Fact]
    public void RejectsUltraEvenWhenResolutionIs1080()
    {
        const string variants = "[720p]https://cdn.example/720.mp4,[1080p]https://cdn.example/1080.mp4,[1080p Ultra]https://cdn.example/ultra.mp4,[1080p <b>ULTRA</b>]https://cdn.example/ultra2.mp4,[2160p]https://cdn.example/2160.mp4";
        var list = StreamParser.Parse(variants);
        Assert.Equal(new[] { 720, 1080, 2160 }, list.Select(x => x.Quality));
        Assert.DoesNotContain(list, x => x.Url.AbsolutePath.Contains("ultra", StringComparison.OrdinalIgnoreCase));
    }}

