using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class LibraryPathTests
{
    [Theory]
    [InlineData("123", "56", "HDrezka-123 - voice-56.strm")]
    [InlineData("1", "0", "HDrezka-1 - voice-0.strm")]
    public void CreatesStableSafeVersionNames(string id, string voice, string expected)
    {
        Assert.Equal(expected, LibraryPath.VersionFileName(id, voice));
        Assert.Equal($"HDrezka-{id}", LibraryPath.TitleDirectory(id));
        Assert.Equal($"HDrezka-{id}.strm", LibraryPath.PrimaryFileName(id));
    }

    [Theory]
    [InlineData("../1", "56")]
    [InlineData("1", "../../x")]
    [InlineData("", "1")]
    public void RejectsInvalidIdentifiers(string id, string voice)
    {
        Assert.Throws<ArgumentException>(() => LibraryPath.VersionFileName(id, voice));
    }
}
