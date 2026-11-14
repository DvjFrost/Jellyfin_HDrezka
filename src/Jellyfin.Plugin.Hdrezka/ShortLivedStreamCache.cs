using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class ShortLivedStreamCache(TimeSpan lifetime, Func<DateTimeOffset>? clock = null)
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, IReadOnlyList<NamedStream> Streams)> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<IReadOnlyList<NamedStream>> GetAsync(string key, Func<CancellationToken, Task<IReadOnlyList<NamedStream>>> fetch, CancellationToken token)
    {
        if (_entries.TryGetValue(key, out var hit) && hit.Expires > _clock()) return hit.Streams;
        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_entries.TryGetValue(key, out hit) && hit.Expires > _clock()) return hit.Streams;
            var streams = await fetch(token).ConfigureAwait(false);
            if (streams.Count > 0)
            {
                if (_entries.Count >= 2000)
                    foreach (var entry in _entries.Where(x => x.Value.Expires <= _clock()).Take(500)) _entries.TryRemove(entry.Key, out _);
                _entries[key] = (_clock().Add(lifetime), streams.ToArray());
            }
            else _entries.TryRemove(key, out _);
            return streams;
        }
        finally { gate.Release(); }
    }

    public void Invalidate(string key) => _entries.TryRemove(key, out _);
}
