using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadQueueTests
{
    [Fact]
    public async Task ProcessesOneJobAtATimeAndPersistsResult()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            var first = NewJob(); var second = NewJob() with { RelativePath = "another.mp4" };
            await queue.AddAsync(first, CancellationToken.None);
            await queue.AddAsync(second, CancellationToken.None);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var current = queue.ProcessNextAsync(async (_, _) => { entered.SetResult(); await release.Task; }, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(await queue.ProcessNextAsync((_, _) => Task.CompletedTask, CancellationToken.None));
            release.SetResult();
            Assert.True(await current);
            Assert.Equal(DownloadJobStatus.Completed, (await queue.ListAsync(CancellationToken.None))[0].Status);
            Assert.Equal(DownloadJobStatus.Completed, (await new DownloadQueueStore(root).LoadAsync(CancellationToken.None))[0].Status);
            Assert.True(await queue.ProcessNextAsync((_, _) => Task.CompletedTask, CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static DownloadJob NewJob() => new(Guid.NewGuid(), "/films/drama/123-test.html", "56", 720, "library", "movie.mp4", null, null, DownloadJobStatus.Queued, null);

    [Fact]
    public async Task RefusesDuplicateActiveDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            await queue.AddAsync(NewJob(), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queue.AddAsync(NewJob(), CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AddsBatchAtomicallyAndRejectsDuplicateNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => queue.AddRangeAsync([NewJob(), NewJob()], CancellationToken.None));
            Assert.Empty(await queue.ListAsync(CancellationToken.None));
            await queue.AddRangeAsync([NewJob(), NewJob() with { RelativePath = "two.mp4" }], CancellationToken.None);
            Assert.Equal(2, (await new DownloadQueueStore(root).LoadAsync(CancellationToken.None)).Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CancellationOfActiveJobIsNotOverwrittenWhenWorkerFinishes()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            var job = NewJob();
            await queue.AddAsync(job, CancellationToken.None);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var processing = queue.ProcessNextAsync(async (_, _) => { entered.SetResult(); await release.Task; }, CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await queue.CancelAsync(job.Id, CancellationToken.None));
            Assert.False(await queue.RemoveAsync(job.Id, CancellationToken.None));
            Assert.Equal(0, await queue.ClearFinishedAsync(CancellationToken.None));
            release.SetResult();
            await processing;
            Assert.Equal(DownloadJobStatus.Cancelled, Assert.Single(await queue.ListAsync(CancellationToken.None)).Status);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task RemovesFinishedHistoryWithoutTouchingQueuedJobs()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            var first = NewJob(); var second = NewJob() with { RelativePath = "two.mp4" };
            await queue.AddRangeAsync([first, second], CancellationToken.None);
            Assert.False(await queue.RemoveAsync(second.Id, CancellationToken.None));
            Assert.True(await queue.ProcessNextAsync((_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.True(await queue.RemoveAsync(first.Id, CancellationToken.None));
            Assert.Single(await queue.ListAsync(CancellationToken.None));
            Assert.True(await queue.CancelAsync(second.Id, CancellationToken.None));
            Assert.Equal(1, await queue.ClearFinishedAsync(CancellationToken.None));
            Assert.Empty(await new DownloadQueueStore(root).LoadAsync(CancellationToken.None));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task KeepsActualQualityWhenJobCompletes()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-worker-" + Guid.NewGuid());
        try
        {
            var queue = await DownloadQueue.OpenAsync(new DownloadQueueStore(root), CancellationToken.None);
            var job = NewJob();
            await queue.AddAsync(job, CancellationToken.None);
            await queue.ProcessNextAsync((item, token) => queue.SetActualQualityAsync(item.Id, 480, token, 640, 360), CancellationToken.None);
            var result = Assert.Single(await queue.ListAsync(CancellationToken.None));
            Assert.Equal(DownloadJobStatus.Completed, result.Status);
            Assert.Equal(480, result.ActualQuality);
            Assert.Equal(640, result.Width);
            Assert.Equal(360, result.Height);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
