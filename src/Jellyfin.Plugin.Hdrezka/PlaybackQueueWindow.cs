namespace Jellyfin.Plugin.Hdrezka;

public static class PlaybackQueueWindow
{
    public const int EarlierEpisodes = 20;
    public static Guid FindStart(IReadOnlyList<Guid> episodes, Guid current)
    {
        for (var i = 0; i < episodes.Count; i++)
            if (episodes[i] == current) return episodes[Math.Max(0, i - EarlierEpisodes)];
        return current;
    }
}
