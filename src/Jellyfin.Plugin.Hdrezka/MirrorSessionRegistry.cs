using System.Collections.Concurrent;
using System.Net;

namespace Jellyfin.Plugin.Hdrezka;

public static class MirrorSessionRegistry
{
    private sealed record Session(HttpClient Client, CookieContainer Cookies, Uri Mirror, MirrorSessionStore? Store);
    private static readonly ConcurrentDictionary<string, Lazy<Session>> Sessions = new(StringComparer.OrdinalIgnoreCase);
    private static MirrorSessionStore? _store;
    public static void Configure(string dataPath) => _store = new MirrorSessionStore(Path.Combine(dataPath, "hdrezka-sessions"));
    private static Session Get(Uri mirror) => Sessions.GetOrAdd(mirror.GetLeftPart(UriPartial.Authority), _ => new Lazy<Session>(() => {
        var store = _store;
        var cookies = store?.LoadAsync(mirror, CancellationToken.None).GetAwaiter().GetResult() ?? new CookieContainer();
        var handler = new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All, CookieContainer = cookies, UseCookies = true };
        return new Session(new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(35) }, cookies, mirror, store);
    })).Value;
    public static HttpClient Client(Uri mirror) => Get(mirror).Client;
    public static async Task SaveAsync(Uri mirror, CancellationToken token)
    {
        var session = Get(mirror);
        if (session.Store is not null) await session.Store.SaveAsync(session.Mirror, session.Cookies, token).ConfigureAwait(false);
    }
}
