namespace Jellyfin.Plugin.Hdrezka;

public static class SitemapLoader
{
    public static async Task<IReadOnlyList<SitemapVideo>> LoadAsync(MirrorClient mirror, CancellationToken token)
    {
        var index = await mirror.GetSitemapAsync(new Uri(mirror.CurrentMirror, "/sitemap.xml"), token).ConfigureAwait(false);
        var maps = SitemapCatalog.ParseIndex(index, mirror.CurrentMirror);
        if (maps.Count == 0) throw new InvalidDataException("Sitemap index contains no supported files.");
        var videos = new Dictionary<string, SitemapVideo>(StringComparer.Ordinal);
        foreach (var map in maps)
        {
            token.ThrowIfCancellationRequested();
            var xml = await mirror.GetSitemapAsync(map, token).ConfigureAwait(false);
            foreach (var video in SitemapCatalog.Parse(xml, mirror.CurrentMirror).Where(x => x.Kind is CatalogKind.Movie or CatalogKind.Series)) videos.TryAdd(video.Id, video);
        }
        if (videos.Count == 0) throw new InvalidDataException("Sitemap contains no supported videos.");
        return videos.Values.ToArray();
    }
}
