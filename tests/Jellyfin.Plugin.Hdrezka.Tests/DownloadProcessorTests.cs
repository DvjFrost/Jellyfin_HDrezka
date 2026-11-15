using System.Net;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadProcessorTests
{
    [Fact]
    public async Task UsesOneListingAndOneStreamRequestThenFallsBackToValidLowerQuality()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-processor-" + Guid.NewGuid());
        var mirrorCalls = 0;
        var requested = new List<string>();
        try
        {
            using var mirrorHttp = new HttpClient(new Handler(request => {
                mirrorCalls++;
                var text = request.Method == HttpMethod.Get
                    ? "<h1>Movie</h1><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>"
                    : "{\"success\":true,\"url\":\"[1080p]https://stream.voidboost.one/playlist.m3u8,[720p]https://stream.voidboost.one/good.mp4\"}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
            }));
            using var cdnHttp = new HttpClient(new Handler(request => {
                requested.Add(request.RequestUri!.AbsolutePath);
                return request.RequestUri.AbsolutePath == "/playlist.m3u8"
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>denied</html>") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m']) };
            }));
            using var mirror = new MirrorClient(new Uri("https://processor.test/"), mirrorHttp);
            var job = new DownloadJob(Guid.NewGuid(), "/films/drama/123-title.html", "56", 1080, "library", "movie.mp4", null, null, DownloadJobStatus.Queued, null);
            var actualQuality = await DownloadProcessor.ExecuteAsync(job, root, mirror, cdnHttp, null, CancellationToken.None);
            Assert.Equal(720, actualQuality);
            Assert.Equal(2, mirrorCalls);
            Assert.Equal(new[] { "/good.mp4" }, requested);
            Assert.True(File.Exists(Path.Combine(root, "movie.mp4")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(callback(request));
    }

    [Fact]
    public async Task ReusesSeriesVoiceListingAcrossEpisodeJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-shared-" + Guid.NewGuid());
        var gets = 0; var posts = 0;
        try
        {
            using var mirrorHttp = new HttpClient(new Handler(request => {
                if (request.Method == HttpMethod.Get) { gets++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<h1>Title</h1><a class='b-translator__items' data-id='123' data-translator_id='56' title='Free'></a>") }; }
                posts++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":true,\"url\":\"[720p]https://stream.voidboost.one/video.mp4\"}") };
            }));
            using var cdnHttp = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m']) }));
            using var mirror = new MirrorClient(new Uri("https://reuse-download.test/"), mirrorHttp);
            var source = new HdrezkaSource(mirror);
            var job = new DownloadJob(Guid.NewGuid(), "/series/test/123-title.html", "56", 720, "library", "one.mp4", 1, 1, DownloadJobStatus.Queued, null);
            await DownloadProcessor.ExecuteAsync(job, root, mirror, cdnHttp, null, CancellationToken.None, source: source);
            await DownloadProcessor.ExecuteAsync(job with { Id = Guid.NewGuid(), RelativePath = "two.mp4", Episode = 2 }, root, mirror, cdnHttp, null, CancellationToken.None, source: source);
            Assert.Equal(1, gets);
            Assert.Equal(2, posts);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
