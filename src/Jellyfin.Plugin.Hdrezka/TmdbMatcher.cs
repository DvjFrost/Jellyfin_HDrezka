namespace Jellyfin.Plugin.Hdrezka;

public sealed class TmdbMatcher(TmdbClient client, TimeSpan delay)
{
    public static string Fingerprint(SitemapVideo video)
    {
        var input = $"v2|{video.Kind}|{video.Year}|{video.Query}";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input)));
    }

    public static bool IsCurrent(SitemapVideo video, TmdbMatch match) => match.QueryFingerprint == Fingerprint(video);

    public async Task<TmdbMatch> MatchAsync(SitemapVideo video, CancellationToken token)
    {
        if (delay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        var types = video.Kind switch { CatalogKind.Movie => new[] { false }, CatalogKind.Series => new[] { true }, _ => new[] { false, true } };
        var queries = new[] { video.Query, TmdbSearch.ToCyrillic(video.Query) }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = new List<(TmdbCandidate Candidate, bool Series)>();
        foreach (var series in types)
        {
            foreach (var query in queries)
            {
                token.ThrowIfCancellationRequested();
                var results = await CatalogRetry.RunAsync(t => client.SearchAsync(query, series, t), token, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                candidates.AddRange(results.Select(r => (r, series)));
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token).ConfigureAwait(false);
            }
        }
        var unique = candidates.DistinctBy(c => (c.Candidate.Id, c.Series)).ToArray();
        var exact = unique.Where(c => TmdbSearch.Choose(video.Query, video.Year, new[] { c.Candidate }) is not null).ToArray();
        if (exact.Length == 1)
        {
            var match = exact[0];
            return new(video.Id, TmdbMatchStatus.Matched, match.Candidate.Id, match.Candidate.Title, match.Candidate.PosterPath, match.Series ? "tv" : "movie", Fingerprint(video));
        }
        return new(video.Id, unique.Length == 0 ? TmdbMatchStatus.NoResults : TmdbMatchStatus.Uncertain, null, null, null, null, Fingerprint(video));
    }
}
