using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MetadataFileLockTests
{
    [Fact]
    public async Task SerializesUpdatesForSamePathAndAllowsIndependentPaths()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var path = Path.Combine(Path.GetTempPath(), "hdrezka-test-" + Guid.NewGuid(), "movie.nfo");
        var first = MetadataFileLock.RunAsync(path, async () => { firstStarted.SetResult(); await release.Task; }, CancellationToken.None);
        await firstStarted.Task;
        var second = MetadataFileLock.RunAsync(path, () => { secondStarted.SetResult(); return Task.CompletedTask; }, CancellationToken.None);
        var other = MetadataFileLock.RunAsync(path + ".other", () => Task.CompletedTask, CancellationToken.None);
        await other.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(secondStarted.Task.IsCompleted);
        release.SetResult(); await Task.WhenAll(first, second);
        Assert.True(secondStarted.Task.IsCompleted);
    }
}
