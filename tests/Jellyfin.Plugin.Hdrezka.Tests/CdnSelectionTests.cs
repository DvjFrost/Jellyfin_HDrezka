using Jellyfin.Plugin.Hdrezka;
using Xunit;

public class CdnSelectionTests
{
    [Fact] public void ReplacesOnlyHostAndKeepsSignedPathAndQuery()
    {
        var input = new Uri("https://stream.voidboost.one/path/video.mp4?token=abc&x=1");
        var result = CdnSelection.Apply(input, "prx.ukrtelcdn.net");
        Assert.Equal("prx.ukrtelcdn.net", result.Host);
        Assert.Equal(input.PathAndQuery, result.PathAndQuery);
        Assert.Equal(input, CdnSelection.Apply(input, ""));
    }
    [Fact] public void RejectsUnknownOrUntrustedHosts()
    {
        var input = new Uri("https://stream.voidboost.one/path");
        Assert.Equal(input, CdnSelection.Apply(input, "example.invalid"));
        Assert.Equal(new Uri("https://other.example/path"), CdnSelection.Apply(new Uri("https://other.example/path"), "prx.ukrtelcdn.net"));
    }
    [Fact] public void UnavailableCdnCannotBeSelectedButCanBeRewrittenAsSource()
    {
        Assert.DoesNotContain("ukrtelcdn.net", CdnSelection.Hosts);
        var source = new Uri("https://stream.voidboost.one/path?token=abc");
        Assert.Equal(source, CdnSelection.Apply(source, "ukrtelcdn.net"));
        var oldSource = new Uri("https://ukrtelcdn.net/path?token=abc");
        Assert.Equal("prx.ukrtelcdn.net", CdnSelection.Apply(oldSource, "prx.ukrtelcdn.net").Host);
    }
}
