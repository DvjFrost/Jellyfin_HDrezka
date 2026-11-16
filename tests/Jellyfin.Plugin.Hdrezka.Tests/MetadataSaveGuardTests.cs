using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using System.Reflection;
using Xunit;

public sealed class MetadataSaveGuardTests
{
    [Fact]
    public async Task ProviderProxySerializesJellyfinSaveWithPluginWriter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrezka-proxy-test-" + Guid.NewGuid().ToString("N"), "HDrezka-321");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "hdrezka.json"), "{}");
            var item = new Movie { Path = Path.Combine(dir, "HDrezka-321.strm") };
            var inner = DispatchProxy.Create<IProviderManager, FakeProvider>();
            var outer = DispatchProxy.Create<IProviderManager, MetadataProviderProxy>();
            ((MetadataProviderProxy)(object)outer).Inner = inner;
            var fake = (FakeProvider)(object)inner;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writer = MetadataFileLock.RunAsync(Path.Combine(dir, "movie.nfo"), async () => { entered.SetResult(); await release.Task; }, CancellationToken.None);
            await entered.Task;
            var saving = outer.SaveMetadataAsync(item, ItemUpdateType.MetadataEdit);
            Assert.Equal(0, fake.Calls);
            release.SetResult();
            await Task.WhenAll(writer, saving);
            Assert.Equal(1, fake.Calls);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    public class FakeProvider : DispatchProxy
    {
        public int Calls { get; private set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task JellyfinSaverWaitsForPluginWriterForSameMovieNfo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrezka-save-test-" + Guid.NewGuid().ToString("N"), "HDrezka-123");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "hdrezka.json"), "{}");
            var item = new Movie { Path = Path.Combine(dir, "HDrezka-123.strm") };
            var nfo = Path.Combine(dir, "movie.nfo");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var plugin = MetadataFileLock.RunAsync(nfo, async () => { entered.SetResult(); await release.Task; }, CancellationToken.None);
            await entered.Task;
            var saved = false;
            var jellyfin = MetadataSaveGuard.RunAsync(item, () => { saved = true; return Task.CompletedTask; });
            Assert.False(saved);
            release.SetResult();
            await Task.WhenAll(plugin, jellyfin);
            Assert.True(saved);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }

    [Fact]
    public async Task OrdinaryMoviesAreNotBlockedByHdrezkaLocks()
    {
        var item = new Movie { Path = Path.Combine(Path.GetTempPath(), "ordinary", "film.mp4") };
        var saved = false;
        await MetadataSaveGuard.RunAsync(item, () => { saved = true; return Task.CompletedTask; });
        Assert.True(saved);
    }

    [Fact]
    public async Task SeriesSaverUsesTheSameLockAsSeriesWriter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hdrezka-series-save-" + Guid.NewGuid().ToString("N"), "HDrezka-456");
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "hdrezka-series.json"), "{}");
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writer = MetadataFileLock.RunAsync(Path.Combine(dir, "tvshow.nfo"), async () => { entered.SetResult(); await release.Task; }, CancellationToken.None);
            await entered.Task;
            var saved = false;
            var saver = MetadataSaveGuard.RunAsync(new Series { Path = dir }, () => { saved = true; return Task.CompletedTask; });
            Assert.False(saved);
            release.SetResult();
            await Task.WhenAll(writer, saver);
            Assert.True(saved);
        }
        finally { Directory.Delete(Path.GetDirectoryName(dir)!, true); }
    }
}
