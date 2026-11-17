namespace Jellyfin.Plugin.Hdrezka;

public static class MultipartContinuation
{
    public static bool Finished(long? position, long? duration, bool failed)
        => !failed && duration is > 0 && position is >= 0 && position >= duration - TimeSpan.FromSeconds(5).Ticks;
}
