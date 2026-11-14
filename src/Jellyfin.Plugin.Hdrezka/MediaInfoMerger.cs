using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.Hdrezka;

public static class MediaInfoMerger
{
    public static bool Apply(MediaSourceInfo target, MediaInfo? probe)
    {
        if (probe is null || string.IsNullOrWhiteSpace(probe.Container) || probe.MediaStreams is null || !probe.MediaStreams.Any(x => x.Type == MediaStreamType.Video)) return false;
        target.Container = probe.Container;
        target.MediaStreams = probe.MediaStreams;
        target.RunTimeTicks = probe.RunTimeTicks;
        target.Bitrate = probe.Bitrate;
        target.Size = probe.Size;
        target.Formats = probe.Formats;
        return true;
    }
}

