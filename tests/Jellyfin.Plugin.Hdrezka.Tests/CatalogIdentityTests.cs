using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class CatalogIdentityTests
{
    [Fact]
    public void IdentitySurvivesMirrorAndSlugChanges()
    {
        Assert.Equal(CatalogIdentity.FromPath("/films/action/123-old.html"), CatalogIdentity.FromPath("/films/drama/123-new.html"));
    }

    [Theory]
    [InlineData("/films/action/no-id.html")]
    [InlineData("/films/action/0-title.html")]
    [InlineData("https://example.org/films/action/123-title.html")]
    [InlineData("/films/../123-title.html")]
    public void RejectsUnusablePaths(string path)
    {
        Assert.Null(CatalogIdentity.FromPath(path));
    }
}
