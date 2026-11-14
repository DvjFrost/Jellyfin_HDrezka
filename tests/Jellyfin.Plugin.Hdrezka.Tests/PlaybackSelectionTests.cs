using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class PlaybackSelectionTests
{
    [Fact]
    public void UsesSelectedVersionAndFallsBackToRequestedItem()
    {
        var item = Guid.NewGuid();
        var selected = Guid.NewGuid().ToString("N");
        Assert.Equal(selected, PlaybackSelection.Resolve(selected, null, item));
        Assert.Equal(selected, PlaybackSelection.Resolve(null, new Payload { MediaSourceId = selected }, item));
        Assert.Equal(item.ToString("N"), PlaybackSelection.Resolve(null, null, item));
    }

    private sealed class Payload { public string? MediaSourceId { get; set; } }
}
