namespace Jellyfin.Plugin.Hdrezka;

public static class SitemapChanges
{
    public static bool NeedsWrite(SitemapVideo video, IReadOnlyDictionary<string, SitemapVideo> previous, bool filesExist)
        => !filesExist || !previous.TryGetValue(video.Id, out var old) || old != video;
}
