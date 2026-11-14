using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.Hdrezka;

public static class MediaSelection
{
    public static IReadOnlyList<NamedStream> BestPerVoice(IEnumerable<NamedStream> streams) => streams
        .Where(x => x.Stream.Quality is > 0 and <= 1080)
        .GroupBy(x => x.Voice, StringComparer.Ordinal)
        .Select(g => g.OrderByDescending(x => x.Stream.Quality).First())
        .ToArray();

    public static async Task<NamedStream?> FirstPlayableAsync(IEnumerable<NamedStream> streams, Func<NamedStream, CancellationToken, Task<bool>> probe, CancellationToken token, int preferredQuality = 0)
    {
        var limit = preferredQuality is >= 1 and <= 1080 ? preferredQuality : 1080;
        foreach (var candidate in streams.Where(x => x.Stream.Quality > 0 && x.Stream.Quality <= limit).OrderByDescending(x => x.Stream.Quality))
        {
            token.ThrowIfCancellationRequested();
            if (await probe(candidate, token).ConfigureAwait(false)) return candidate;
        }
        return null;
    }

    public static string SourceId(string itemId, string voice)
    {
        var input = Encoding.UTF8.GetBytes(itemId + "\0" + voice);
        var hash = SHA256.HashData(input);
        return new Guid(hash.AsSpan(0, 16)).ToString("N");
    }
}
