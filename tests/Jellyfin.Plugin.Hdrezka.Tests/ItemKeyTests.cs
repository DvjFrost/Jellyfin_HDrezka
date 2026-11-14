using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class ItemKeyTests
{
    [Fact] public void RoundTripsOnlyRelativeMirrorPaths()
    {
        var encoded = ItemKey.EncodePath("/series/drama/123-title.html");
        Assert.True(ItemKey.TryDecodePath(encoded, out var path));
        Assert.Equal("/series/drama/123-title.html", path);
    }
    [Theory]
    [InlineData("//evil.example/movie")]
    [InlineData("/films/../admin")]
    [InlineData("/films/%2e%2e/admin")]
    [InlineData("https://evil.example/")]
    public void RejectsEscapingMirrorPaths(string path)
    {
        Assert.Throws<ArgumentException>(() => ItemKey.EncodePath(path));
    }
}
