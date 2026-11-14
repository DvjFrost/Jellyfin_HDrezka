using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class PlaybackQueueWindowTests
{
    [Fact] public void IncludesPreviousEpisodesAndRetainsUpcomingCapacity()
    {
        var ids = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var result = PlaybackQueueWindow.FindStart(ids, ids[70]);
        Assert.Equal(ids[50], result);
    }
    [Fact] public void IncludesBeginningWhenFewerThanTwentyPrecede()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(ids[0], PlaybackQueueWindow.FindStart(ids, ids[3]));
    }
    [Fact] public void DoesNotChangeUnknownEpisode()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, PlaybackQueueWindow.FindStart([Guid.NewGuid()], id));
    }
}
