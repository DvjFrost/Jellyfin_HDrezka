namespace Jellyfin.Plugin.Hdrezka;

public sealed record TmdbIndexSummary(int Total, int Processed, int Matched, int Uncertain, int NoResults);

public sealed class TmdbIndex(TmdbMatcher matcher, TmdbMatchStore store)
{
    public async Task<TmdbIndexSummary> RunAsync(IReadOnlyList<SitemapVideo> videos, CancellationToken token, IProgress<TmdbIndexSummary>? progress = null, Func<SitemapVideo,TmdbMatch,CancellationToken,Task>? onItem = null)
    {
        var saved = (await store.LoadAsync(token).ConfigureAwait(false)).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var currentVideos = videos.DistinctBy(x => x.Id).ToDictionary(x => x.Id, StringComparer.Ordinal);
        TmdbIndexSummary Summary()
        {
            var matches = saved.Values.Where(x => currentVideos.TryGetValue(x.Id, out var video) && TmdbMatcher.IsCurrent(video, x)).ToArray();
            return new(currentVideos.Count, matches.Length, matches.Count(x => x.Status == TmdbMatchStatus.Matched), matches.Count(x => x.Status == TmdbMatchStatus.Uncertain), matches.Count(x => x.Status == TmdbMatchStatus.NoResults));
        }
        var visited = 0;
        foreach (var video in videos)
        {
            token.ThrowIfCancellationRequested();
            if (!saved.TryGetValue(video.Id, out var match) || !TmdbMatcher.IsCurrent(video, match))
            {
                match = await matcher.MatchAsync(video, token).ConfigureAwait(false);
                await store.AppendAsync(match, token).ConfigureAwait(false);
                saved[video.Id] = match;
            }
            if (onItem is not null) await onItem(video, match, token).ConfigureAwait(false);
            if (++visited % 100 == 0) progress?.Report(Summary() with { Processed = visited });
        }
        return Summary();
    }
}
