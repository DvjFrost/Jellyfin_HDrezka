using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class LibraryShortcutRepairTask(ILibraryManager library, LibraryMediaProvider media, ILogger<LibraryShortcutRepairTask> logger) : IScheduledTask
{
    public string Name => "Проверить файлы HDrezka";
    public string Description => "Исправить служебные признаки потоков в локальной медиатеке без запросов к сайту.";
    public string Category => "HDrezka";
    public string Key => "HdrezkaRepairShortcuts";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [new() { Type = TaskTriggerInfoType.StartupTrigger }, new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromDays(1).Ticks }];
    public static bool IsCandidate(Video item) => LibraryVersionTitles.NeedsShortcutRepair(item);

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        while (library.IsScanRunning) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var root = Path.GetFullPath(media.Root);
        var paths = new[] { "films", "series", "cartoons", "animation", "show" }
            .Select(x => Path.GetFullPath(Path.Combine(root, x))).ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var folders = library.GetVirtualFolders().Where(x => x.Locations.Any(p => paths.Contains(Path.GetFullPath(p))))
            .Select(x => Guid.TryParse(x.ItemId, out var id) ? library.GetItemById(id) as Folder : null)
            .OfType<Folder>().DistinctBy(x => x.Id).ToArray();
        var repaired = 0;
        var seen = new HashSet<Guid>();
        for (var i = 0; i < folders.Length; i++)
        {
            foreach (var primary in folders[i].GetRecursiveChildren().OfType<Video>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = new List<Video> { primary };
                if (primary.LocalAlternateVersions.Length > 0)
                    candidates.AddRange(library.GetLocalAlternateVersionIds(primary).Select(library.GetItemById).OfType<Video>());
                foreach (var item in candidates.Where(IsCandidate))
                {
                    if (!seen.Add(item.Id) || media.Record(item) is null || !LibraryVersionTitles.RepairShortcut(item)) continue;
                    await library.UpdateItemAsync(item, item.GetParent(), ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                    repaired++;
                }
            }
            progress.Report((i + 1) * 100.0 / folders.Length);
        }
        logger.LogInformation("HDrezka shortcut check repaired {Count} entries", repaired);
        progress.Report(100);
    }
}
