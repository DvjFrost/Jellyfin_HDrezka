using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MultipartContinuationTests
{
    [Fact]
    public void AdvancesOnlyAtEndOfSuccessfulPart()
    {
        var duration = TimeSpan.FromMinutes(80).Ticks;
        Assert.True(MultipartContinuation.Finished(duration - TimeSpan.FromSeconds(1).Ticks, duration, false));
        Assert.False(MultipartContinuation.Finished(duration - TimeSpan.FromMinutes(1).Ticks, duration, false));
        Assert.False(MultipartContinuation.Finished(duration - TimeSpan.FromSeconds(1).Ticks, duration, true));
        Assert.False(MultipartContinuation.Finished(null, duration, false));
        Assert.False(MultipartContinuation.Finished(100, null, false));
    }
}
