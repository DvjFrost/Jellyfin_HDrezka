using System.Reflection;
using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class LibraryBatchPersistenceTests
{
    [Fact]
    public void SplitsOnlyOwnedLargeInsertAndPreservesOrder()
    {
        var data = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hdrezka-batch-test"));
        var root = Path.Combine(data, "hdrezka-library");
        var chunks = new List<BaseItem[]>();
        var inner = DispatchProxy.Create<IItemPersistenceService, Capture>();
        ((Capture)(object)inner).Handler = (m, args) => { if (m.Name == nameof(IItemPersistenceService.SaveItems)) chunks.Add(((IReadOnlyList<BaseItem>)args![0]!).ToArray()); return null; };
        var paths = DispatchProxy.Create<IApplicationPaths, Capture>();
        ((Capture)(object)paths).Handler = (m, _) => m.Name == "get_ProgramDataPath" ? data : null;
        var wrapper = new LibraryBatchPersistence(inner, paths, NullLogger<LibraryBatchPersistence>.Instance);
        var items = Enumerable.Range(0, 1201).Select(i => (BaseItem)new Movie { Id = Guid.NewGuid(), Path = Path.Combine(root, "films", "HDrezka-" + i) }).ToArray();
        wrapper.SaveItems(items, CancellationToken.None);
        Assert.Equal(new[] { 500, 500, 201 }, chunks.Select(x => x.Length));
        Assert.Equal(items, chunks.SelectMany(x => x));
        chunks.Clear();
        foreach (var item in items) item.Path = Path.Combine(root + "-other", "films", item.Id.ToString());
        wrapper.SaveItems(items, CancellationToken.None);
        Assert.Equal(1201, Assert.Single(chunks).Length);
    }

    [Fact]
    public void KeepsLargeCatalogUnderSqliteParameterLimit()
    {
        var data = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hdrezka-large-batch"));
        var inner = DispatchProxy.Create<IItemPersistenceService, Capture>();
        var calls = 0;
        ((Capture)(object)inner).Handler = (m, args) => {
            if (m.Name == nameof(IItemPersistenceService.SaveItems))
            {
                var count = ((IReadOnlyList<BaseItem>)args![0]!).Count;
                Assert.InRange(count, 1, 500);
                calls++;
            }
            return null;
        };
        var paths = DispatchProxy.Create<IApplicationPaths, Capture>();
        ((Capture)(object)paths).Handler = (m, _) => m.Name == "get_ProgramDataPath" ? data : null;
        var item = new Movie { Path = Path.Combine(data, "hdrezka-library", "films", "HDrezka-1") };
        var items = Enumerable.Repeat((BaseItem)item, 40000).ToArray();
        new LibraryBatchPersistence(inner, paths, NullLogger<LibraryBatchPersistence>.Instance).SaveItems(items, CancellationToken.None);
        Assert.Equal(80, calls);
    }

    public class Capture : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
}
