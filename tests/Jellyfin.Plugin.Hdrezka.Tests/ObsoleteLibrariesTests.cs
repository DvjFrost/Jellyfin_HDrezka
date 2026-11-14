using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class ObsoleteLibrariesTests
{
    [Fact]
    public void OnlyRemovesAnExactOwnedLibrary()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hdrezka-test"));
        var owned = Path.Combine(root, "animation");
        Assert.True(SitemapLibraries.IsObsoleteOwnedLibrary("Аниме HDrezka", [owned], root));
        Assert.False(SitemapLibraries.IsObsoleteOwnedLibrary("Аниме HDrezka", [Path.Combine(root, "other")], root));
        Assert.False(SitemapLibraries.IsObsoleteOwnedLibrary("Аниме HDrezka", [owned, Path.Combine(root, "other")], root));
        Assert.False(SitemapLibraries.IsObsoleteOwnedLibrary("Фильмы HDrezka", [Path.Combine(root, "films")], root));
    }
}
