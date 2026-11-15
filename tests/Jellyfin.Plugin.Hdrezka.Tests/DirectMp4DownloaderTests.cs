using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DirectMp4DownloaderTests
{
    private static readonly byte[] Mp4 = [0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];

    [Fact]
    public async Task WritesOnlyValidMp4AndNeverOverwrites()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mp4) }));
            var path = Path.Combine(root, "movie.mp4");
            await DirectMp4Downloader.DownloadAsync(client, new Uri("https://cdn.example/video"), path, CancellationToken.None);
            Assert.Equal(Mp4, await File.ReadAllBytesAsync(path));
            await Assert.ThrowsAsync<IOException>(() => DirectMp4Downloader.DownloadAsync(client, new Uri("https://cdn.example/video"), path, CancellationToken.None));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RejectsHtmlAndDeletesPartialFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-test-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Access denied</html>") }));
            await Assert.ThrowsAsync<InvalidDataException>(() => DirectMp4Downloader.DownloadAsync(client, new Uri("https://cdn.example/video"), Path.Combine(root, "movie.mp4"), CancellationToken.None));
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response());
    }

    [Fact]
    public async Task FollowsKnownCdnRedirectButRejectsUntrustedTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-redirect-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var client = new HttpClient(new RedirectHandler());
            await DirectMp4Downloader.DownloadAsync(client, new Uri("https://stream.voidboost.one/start"), Path.Combine(root, "movie.mp4"), CancellationToken.None);
            Assert.Equal(Mp4, await File.ReadAllBytesAsync(Path.Combine(root, "movie.mp4")));
            await Assert.ThrowsAsync<InvalidDataException>(() => DirectMp4Downloader.DownloadAsync(client, new Uri("https://stream.voidboost.one/unsafe"), Path.Combine(root, "other.mp4"), CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/final") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mp4) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = request.RequestUri.AbsolutePath == "/unsafe" ? new Uri("http://127.0.0.1/private") : new Uri("https://prx.ukrtelcdn.net/final") } });
        }
    }

    [Fact]
    public async Task TimesOutWhenCdnDoesNotSendResponseHeaders()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-timeout-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using var client = new HttpClient(new HangingHandler());
            await Assert.ThrowsAsync<TimeoutException>(() => DirectMp4Downloader.DownloadAsync(client, new Uri("https://stream.voidboost.one/video"), Path.Combine(root, "movie.mp4"), CancellationToken.None, idleTimeout: TimeSpan.FromMilliseconds(50)));
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RemovesOnlyItsOwnStalePartialAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-resume-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var id = Guid.NewGuid();
            var stale = Path.Combine(root, ".hdrezka-" + id.ToString("N") + ".part");
            var other = Path.Combine(root, ".hdrezka-" + Guid.NewGuid().ToString("N") + ".part");
            await File.WriteAllTextAsync(stale, "old partial");
            await File.WriteAllTextAsync(other, "unrelated");
            using var client = new HttpClient(new Handler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Mp4) }));
            await DirectMp4Downloader.DownloadAsync(client, new Uri("https://stream.voidboost.one/video"), Path.Combine(root, "movie.mp4"), CancellationToken.None, jobId: id);
            Assert.False(File.Exists(stale));
            Assert.Equal("unrelated", await File.ReadAllTextAsync(other));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
