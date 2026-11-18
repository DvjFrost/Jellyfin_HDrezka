using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MirrorSessionStoreTests
{
    [Fact]
    public async Task SessionResetClearsCookiesWithoutReplacingHttpClient()
    {
        var uri = new Uri("https://session-reset-test.invalid/");
        var client = MirrorSessionRegistry.Client(uri);
        MirrorSessionRegistry.CookiesForTesting(uri).Add(uri, new Cookie("session", "old", "/"));
        await MirrorSessionRegistry.ResetAsync(uri, CancellationToken.None);
        Assert.Same(client, MirrorSessionRegistry.Client(uri));
        Assert.Empty(MirrorSessionRegistry.CookiesForTesting(uri).GetCookies(uri).Cast<Cookie>());
    }
    [Fact]
    public async Task PersistsAndRestoresCookiesPerMirror()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hdrezka-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var uri = new Uri("https://mirror.test/");
            var store = new MirrorSessionStore(directory);
            var jar = new CookieContainer();
            jar.Add(uri, new Cookie("anubis", "value", "/") { Secure = true, HttpOnly = true });
            await store.SaveAsync(uri, jar, CancellationToken.None);
            var restored = await store.LoadAsync(uri, CancellationToken.None);
            Assert.Equal("value", restored.GetCookies(uri)["anubis"]?.Value);
            Assert.Empty((await store.LoadAsync(new Uri("https://other.test/"), CancellationToken.None)).GetAllCookies().Cast<Cookie>());
        }
        finally { Directory.Delete(directory, true); }
    }
}
