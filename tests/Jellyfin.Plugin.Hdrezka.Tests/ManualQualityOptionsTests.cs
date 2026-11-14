using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class ManualQualityOptionsTests
{
    [Fact]
    public void ListsOnlyAvailableOrdinaryQualitiesWithStableSeparateIds()
    {
        NamedStream S(int quality) => new("Озвучка", new VideoStream(quality, new Uri($"https://example.test/{quality}")));
        var options = ManualQualityOptions.Create("version-a", "Озвучка", [S(720), S(1080), S(2160), S(720), S(480)]);
        Assert.Equal(new[] { 1080, 720, 480 }, options.Select(x => x.Quality));
        Assert.All(options, x => Assert.True(Guid.TryParse(x.Id, out _)));
        Assert.Equal(3, options.Select(x => x.Id).Distinct().Count());
        Assert.Equal(options[0].Id, ManualQualityOptions.Create("version-a", "Озвучка", [S(1080)])[0].Id);
        Assert.NotEqual(options[0].Id, ManualQualityOptions.Create("version-b", "Озвучка", [S(1080)])[0].Id);
        Assert.Equal("Озвучка — 1080p", options[0].Name);
    }
}
