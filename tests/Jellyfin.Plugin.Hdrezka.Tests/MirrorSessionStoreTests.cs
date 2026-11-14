using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MirrorSessionStoreTests
{
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
