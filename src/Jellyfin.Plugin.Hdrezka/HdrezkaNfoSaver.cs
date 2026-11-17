using System.Collections.Concurrent;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Hdrezka;

// Delegates the actual NFO format to Jellyfin and shares the plugin's per-file
// lock with all metadata updates, including Jellyfin's own saves.
public sealed class HdrezkaNfoSaver(IServiceProvider services, LibraryMediaProvider media) : IMetadataFileSaver
{
    public const string SaverName = "HDrezka NFO";
    private readonly ConcurrentDictionary<string, IMetadataFileSaver> _savers = new(StringComparer.Ordinal);
    public string Name => SaverName;

    public bool IsEnabledFor(BaseItem item, ItemUpdateType updateType)
    {
        if (string.IsNullOrWhiteSpace(item.Path)) return false;
        try
        {
            var relative = Path.GetRelativePath(Path.GetFullPath(media.Root), Path.GetFullPath(item.Path));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return false;
            var section = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!section.Equals("films", StringComparison.OrdinalIgnoreCase) && !section.Equals("series", StringComparison.OrdinalIgnoreCase)) return false;
            return GetSaver(item)?.IsEnabledFor(item, updateType) == true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { return false; }
    }

    public string GetSavePath(BaseItem item) => GetSaver(item)?.GetSavePath(item) ?? throw new InvalidOperationException("No Jellyfin NFO saver for this item.");

    public Task SaveAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var saver = GetSaver(item) ?? throw new InvalidOperationException("No Jellyfin NFO saver for this item.");
        return NfoWriteCoordinator.SaveAsync(saver.GetSavePath(item), () => saver.SaveAsync(item, cancellationToken), cancellationToken);
    }

    private IMetadataFileSaver? GetSaver(BaseItem item)
    {
        var name = item switch
        {
            MediaBrowser.Controller.Entities.TV.Episode => "EpisodeNfoSaver",
            Season => "SeasonNfoSaver",
            Series => "SeriesNfoSaver",
            Video => "MovieNfoSaver",
            _ => null
        };
        if (name is null) return null;
        return _savers.GetOrAdd(name, key =>
        {
            var type = Type.GetType("MediaBrowser.XbmcMetadata.Savers." + key + ", MediaBrowser.XbmcMetadata", throwOnError: true)!;
            return (IMetadataFileSaver)ActivatorUtilities.CreateInstance(services, type);
        });
    }
}
