using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadQueueStoreTests
{
    [Fact]
    public async Task SavesJobsAndResetsInterruptedWorkOnRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-queue-" + Guid.NewGuid());
        try
        {
            var store = new DownloadQueueStore(root);
            var job = new DownloadJob(Guid.NewGuid(), "/films/drama/123-test.html", "56", 720, "library", "movie.mp4", null, null, DownloadJobStatus.Downloading, null);
            await store.SaveAsync([job], CancellationToken.None);
            var jobs = await store.LoadAsync(CancellationToken.None);
            var restored = Assert.Single(jobs);
            Assert.Equal(DownloadJobStatus.Queued, restored.Status);
            Assert.Equal(job.Id, restored.Id);
            Assert.DoesNotContain("http", await File.ReadAllTextAsync(Path.Combine(root, "queue.json")), StringComparison.OrdinalIgnoreCase);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingQueueReturnsEmpty()
    {
        var store = new DownloadQueueStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }
}
