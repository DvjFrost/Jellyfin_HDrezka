using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class MirrorSessionStore(string directory)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private sealed record StoredCookie(string Name, string Value, string Domain, string Path, bool Secure, bool HttpOnly, DateTime Expires);
    private string FileFor(Uri mirror) => Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mirror.GetLeftPart(UriPartial.Authority).ToLowerInvariant()))) + ".json");
    private static bool Allowed(Uri mirror, string domain)
    {
        var host = domain.TrimStart('.');
        return host.Equals(mirror.Host, StringComparison.OrdinalIgnoreCase) ||
            (mirror.Host.Equals("rezka-tv.org", StringComparison.OrdinalIgnoreCase) && host.Equals("hdrezka.la", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<CookieContainer> LoadAsync(Uri mirror, CancellationToken token)
    {
        var container = new CookieContainer();
        var path = FileFor(mirror);
        if (!File.Exists(path)) return container;
        try
        {
            var records = JsonSerializer.Deserialize<List<StoredCookie>>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? [];
            foreach (var item in records.Where(x => Allowed(mirror, x.Domain) && (x.Expires == DateTime.MinValue || x.Expires.ToUniversalTime() > DateTime.UtcNow)))
            {
                try { container.Add(new Cookie(item.Name, item.Value, item.Path, item.Domain) { Secure = item.Secure, HttpOnly = item.HttpOnly, Expires = item.Expires }); }
                catch (CookieException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        return container;
    }

    public async Task SaveAsync(Uri mirror, CookieContainer cookies, CancellationToken token)
    {
        var path = FileFor(mirror);
        var gate = Locks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(directory);
            var records = cookies.GetAllCookies().Cast<Cookie>().Where(x => !x.Expired && Allowed(mirror, x.Domain))
                .Select(x => new StoredCookie(x.Name, x.Value, x.Domain, x.Path, x.Secure, x.HttpOnly, x.Expires)).ToArray();
            await MovieLibraryWriter.AtomicWriteAsync(path, JsonSerializer.Serialize(records), token).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally { gate.Release(); }
    }
}
