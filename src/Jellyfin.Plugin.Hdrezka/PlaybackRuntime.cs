using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.Hdrezka;

public static class PlaybackRuntime
{
    public static bool TryFill(Video item, long? duration)
    {
        if (item.RunTimeTicks is > 0 || duration is null or <= 0 || duration > TimeSpan.TicksPerDay * 7) return false;
        item.RunTimeTicks = duration;
        return true;
    }
}
