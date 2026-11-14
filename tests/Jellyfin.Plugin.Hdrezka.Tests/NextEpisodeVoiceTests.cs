using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class NextEpisodeVoiceTests
{
    [Fact]
    public void ContinuesSelectedVoiceOnlyForNextEpisodeAndSameDevice()
    {
        var tracker = new NextEpisodeVoice();
        var first = new LibraryVideoRecord("123", "/series/a/123-test.html", "1", "1", []);
        var second = first with { Number = "2" };
        tracker.Observe("user", "phone", first, "42", 1000);
        tracker.Stopped("user", "phone", first, 990);
        Assert.Equal("42", tracker.ForNext("user", "phone", second));
        Assert.Null(tracker.ForNext("user", "tv", second));
        Assert.Null(tracker.ForNext("other", "phone", second));
        Assert.Null(tracker.ForNext("user", "phone", first with { Number = "3" }));
        tracker.ClearForViewedEpisode("user", "phone", second);
        Assert.Null(tracker.ForNext("user", "phone", second));
    }

    [Fact]
    public void DoesNotContinueWhenStoppedEarly()
    {
        var tracker = new NextEpisodeVoice();
        var first = new LibraryVideoRecord("123", "/series/a/123-test.html", "1", "1", []);
        tracker.Observe("user", "phone", first, "42", 1000);
        tracker.Stopped("user", "phone", first, 500);
        Assert.Null(tracker.ForNext("user", "phone", first with { Number = "2" }));
    }
}
