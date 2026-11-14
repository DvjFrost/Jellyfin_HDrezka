using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MediaSelectionTests
{
    [Fact]
    public async Task PreferredQualityFallsBackOnlyToLowerQualities()
    {
        NamedStream S(int q) => new("A", new VideoStream(q, new Uri($"https://example.test/{q}")));
        var attempts = new List<int>();
        var chosen = await MediaSelection.FirstPlayableAsync([S(1080), S(720), S(480), S(360)], (candidate, _) => {
            attempts.Add(candidate.Stream.Quality);
            return Task.FromResult(candidate.Stream.Quality == 360);
        }, CancellationToken.None, 720);
        Assert.Equal(360, chosen!.Stream.Quality);
        Assert.Equal(new[] { 720, 480, 360 }, attempts);
    }
    [Fact]
    public void SelectsOneAtMost1080PerVoiceWithoutReorderingVoices()
    {
        NamedStream S(string v, int q) => new(v, new VideoStream(q, new Uri($"https://example.test/{q}")));
        var result = MediaSelection.BestPerVoice([S("A",720),S("A",2160),S("A",1080),S("B",480),S("B",720),S("C",2160)]);
        Assert.Equal(new[] { "A", "B" }, result.Select(x => x.Voice));
        Assert.Equal(new[] { 1080, 720 }, result.Select(x => x.Stream.Quality));
    }
    [Fact]
    public void StableSourceIdsAreGuidsAndDifferentForDifferentVoicesAndItems()
    {
        var a = MediaSelection.SourceId("movie-a", "voice-a");
        Assert.True(Guid.TryParse(a, out _));
        Assert.Equal(a, MediaSelection.SourceId("movie-a", "voice-a"));
        Assert.NotEqual(a, MediaSelection.SourceId("movie-a", "voice-b"));
        Assert.NotEqual(a, MediaSelection.SourceId("movie-b", "voice-a"));
    }

    [Fact]
    public async Task TriesLowerQualityWhenHigherQualityCannotBeProbed()
    {
        NamedStream S(int q) => new("A", new VideoStream(q, new Uri($"https://example.test/{q}")));
        var attempted = new List<int>();
        var selected = await MediaSelection.FirstPlayableAsync([S(720), S(2160), S(1080), S(480)], (candidate, _) =>
        {
            attempted.Add(candidate.Stream.Quality);
            return Task.FromResult(candidate.Stream.Quality == 720);
        }, CancellationToken.None);
        Assert.Equal(720, selected!.Stream.Quality);
        Assert.Equal(new[] { 1080, 720 }, attempted);
    }
}
