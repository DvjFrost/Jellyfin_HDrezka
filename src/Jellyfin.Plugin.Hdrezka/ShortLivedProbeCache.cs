using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class ShortLivedProbeCache<T>(TimeSpan lifetime, Func<DateTimeOffset>? clock = null) where T : class
{
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expiry, T Value)> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 128).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<T?> GetAsync(string key, Func<CancellationToken, Task<T?>> fetch, Func<T, bool> valid, CancellationToken token)
    {
        if (_entries.TryGetValue(key, out var hit) && hit.Expiry > _clock()) return hit.Value;
        var gate = _gates[(StringComparer.Ordinal.GetHashCode(key) & int.MaxValue) % _gates.Length];
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_entries.TryGetValue(key, out hit) && hit.Expiry > _clock()) return hit.Value;
            var value = await fetch(token).ConfigureAwait(false);
            if (value is null || !valid(value)) { _entries.TryRemove(key, out _); return null; }
            if (_entries.Count >= 2000)
                foreach (var entry in _entries.Where(x => x.Value.Expiry <= _clock()).Take(500)) _entries.TryRemove(entry.Key, out _);
            _entries[key] = (_clock().Add(lifetime), value);
            return value;
        }
        finally { gate.Release(); }
    }
}
