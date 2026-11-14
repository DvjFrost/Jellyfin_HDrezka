using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class LibraryBatchPersistence(IItemPersistenceService inner, IApplicationPaths paths, ILogger<LibraryBatchPersistence> logger) : IItemPersistenceService
{
    private const int BatchSize = 500;

    public void SaveItems(IReadOnlyList<BaseItem> items, CancellationToken cancellationToken)
    {
        if (items.Count <= BatchSize || !AreOwned(items))
        {
            inner.SaveItems(items, cancellationToken);
            return;
        }
        logger.LogInformation("HDrezka library save: splitting {Count} items into batches of {BatchSize}", items.Count, BatchSize);
        foreach (var batch in items.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            inner.SaveItems(batch, cancellationToken);
        }
    }

    private bool AreOwned(IReadOnlyList<BaseItem> items)
    {
        try
        {
            var data = Path.GetFullPath(paths.ProgramDataPath);
            var configured = Plugin.Instance?.Configuration.LibraryRoot;
            var roots = new List<string> { LibraryStorage.ResolveRoot(data, configured) };
            if (string.IsNullOrWhiteSpace(configured)) roots.Add(Path.Combine(Directory.GetParent(data)?.FullName ?? data, "hdrezka-library"));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefixes = roots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar).ToArray();
            return items.All(item => !string.IsNullOrEmpty(item.Path) && prefixes.Any(prefix => Path.GetFullPath(item.Path).StartsWith(prefix, comparison)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public void DeleteItem(params IReadOnlyList<Guid> ids) => inner.DeleteItem(ids);
    public Task SaveImagesAsync(BaseItem item, CancellationToken cancellationToken = default) => inner.SaveImagesAsync(item, cancellationToken);
    public Task ReattachUserDataAsync(BaseItem item, CancellationToken cancellationToken) => inner.ReattachUserDataAsync(item, cancellationToken);
    public void UpdateInheritedValues() => inner.UpdateInheritedValues();
}
