using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public static class MetadataFileLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public static async Task<T> ReadAsync<T>(string path, Func<T> read, CancellationToken token)
    {
        T result = default!;
        await RunAsync(path, () => { result = read(); return Task.CompletedTask; }, token).ConfigureAwait(false);
        return result;
    }
    public static async Task RunAsync(string path, Func<Task> action, CancellationToken token)
    {
        var gate = Gates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { await action().ConfigureAwait(false); }
        finally { gate.Release(); }
    }
}
