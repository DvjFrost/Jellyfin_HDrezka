using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Controller.Entities.TV;
using Xunit;

public sealed class PlaybackRuntimeTests
{
    [Fact]
    public void StoresOnlyActualPositiveDurationAndPreservesExistingRuntime()
    {
        var episode = new MediaBrowser.Controller.Entities.TV.Episode();
        Assert.False(PlaybackRuntime.TryFill(episode, null));
        Assert.False(PlaybackRuntime.TryFill(episode, 0));
        Assert.True(PlaybackRuntime.TryFill(episode, TimeSpan.FromMinutes(44).Ticks));
        Assert.Equal(TimeSpan.FromMinutes(44).Ticks, episode.RunTimeTicks);
        Assert.False(PlaybackRuntime.TryFill(episode, TimeSpan.FromMinutes(50).Ticks));
        Assert.Equal(TimeSpan.FromMinutes(44).Ticks, episode.RunTimeTicks);
    }
}
