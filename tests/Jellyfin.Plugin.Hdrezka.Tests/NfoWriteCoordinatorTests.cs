using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class NfoWriteCoordinatorTests
{
    [Fact]
    public async Task SerializesPluginAndJellyfinWritesToSameFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "hdrezka-lock-" + Guid.NewGuid().ToString("N") + ".nfo");
        var active = 0; var maximum = 0;
        async Task Write()
        {
            var count = Interlocked.Increment(ref active);
            if (count > maximum) maximum = count;
            await Task.Delay(10);
            Interlocked.Decrement(ref active);
        }
        await Task.WhenAll(Enumerable.Range(0, 10).Select(i => i % 2 == 0
            ? NfoWriteCoordinator.SaveAsync(path, Write, CancellationToken.None)
            : MetadataFileLock.RunAsync(path, Write, CancellationToken.None)));
        Assert.Equal(1, maximum);
    }

    [Fact]
    public async Task ReadersWaitForTheJellyfinSave()
    {
        var path = Path.Combine(Path.GetTempPath(), "hdrezka-read-" + Guid.NewGuid().ToString("N") + ".nfo");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = NfoWriteCoordinator.SaveAsync(path, async () => { started.SetResult(); await release.Task; }, CancellationToken.None);
        await started.Task;
        var reader = MetadataFileLock.ReadAsync(path, () => "complete", CancellationToken.None);
        Assert.False(reader.IsCompleted);
        release.SetResult();
        await writer;
        Assert.Equal("complete", await reader);
    }
}
