using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Xunit;

public sealed class MediaInfoMergerTests
{
    [Fact]
    public void CopiesActualProbeInformationWithoutReplacingUrlOrHeaders()
    {
        var target = new MediaSourceInfo { Path = "https://example.test/video", RequiredHttpHeaders = new() { ["Referer"] = "https://mirror.test/" } };
        var probe = new MediaInfo { Container = "mp4", MediaStreams = [new MediaStream { Type = MediaStreamType.Video, Codec = "h264" }, new MediaStream { Type = MediaStreamType.Audio, Codec = "aac" }], RunTimeTicks = 123, Bitrate = 456 };
        Assert.True(MediaInfoMerger.Apply(target, probe));
        Assert.Equal("mp4", target.Container); Assert.Equal(2, target.MediaStreams.Count); Assert.Equal(123, target.RunTimeTicks);
        Assert.Equal("https://example.test/video", target.Path); Assert.Equal("https://mirror.test/", target.RequiredHttpHeaders["Referer"]);
    }
    [Fact]
    public void RejectsEmptyAndAudioOnlyProbes()
    {
        var target = new MediaSourceInfo();
        Assert.False(MediaInfoMerger.Apply(target, null));
        Assert.False(MediaInfoMerger.Apply(target, new MediaInfo { Container = "mp4", MediaStreams = [new MediaStream { Type = MediaStreamType.Audio, Codec = "aac" }] }));
        Assert.Empty(target.MediaStreams);
    }
}
