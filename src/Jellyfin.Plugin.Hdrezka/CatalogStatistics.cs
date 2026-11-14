namespace Jellyfin.Plugin.Hdrezka;

public sealed record CatalogStatistics(int Total, int Processed, int Matched, int Uncertain, int NoResults, int Remaining)
{
    public static CatalogStatistics Calculate(IEnumerable<SitemapVideo> videos, IReadOnlyDictionary<string, TmdbMatch> matches)
    {
        var current = videos.Where(x => x.Kind is CatalogKind.Movie or CatalogKind.Series).DistinctBy(x => x.Id).ToArray();
        var results = current.Select(x => matches.TryGetValue(x.Id, out var m) && TmdbMatcher.IsCurrent(x, m) ? m : null).Where(x => x is not null).ToArray();
        return new(current.Length, results.Length, results.Count(x => x!.Status == TmdbMatchStatus.Matched), results.Count(x => x!.Status == TmdbMatchStatus.Uncertain), results.Count(x => x!.Status == TmdbMatchStatus.NoResults), current.Length - results.Length);
    }
}
