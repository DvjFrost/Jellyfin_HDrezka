using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public static class LibraryVersionTitles
{
    public static bool NeedsShortcutRepair(Video item) => !item.IsShortcut && !string.IsNullOrEmpty(item.Path) &&
        Regex.IsMatch(Path.GetFileName(item.Path), @"^HDrezka-[0-9]+(?: S[0-9]+E[0-9]+)?(?: - voice-[0-9]{1,16})?\.strm$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool RepairShortcut(Video item)
    {
        if (!NeedsShortcutRepair(item)) return false;
        item.IsShortcut = true;
        return true;
    }

    public static bool ShouldRepair(string? current, string? path, string? primaryName) =>
        !string.IsNullOrWhiteSpace(primaryName) && !string.IsNullOrEmpty(path)
        && string.Equals(current, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal)
        && Regex.IsMatch(Path.GetFileName(path), @"^HDrezka-[0-9]+.* - voice-[0-9]{1,16}\.strm$", RegexOptions.CultureInvariant);

    public static bool ShouldSyncAlternateName(string? current, string? primary, bool locked) =>
        !locked && !string.IsNullOrWhiteSpace(primary) && !string.Equals(current, primary, StringComparison.Ordinal);

    public static async Task RepairAsync(Folder folder, ILibraryManager library, LibraryMediaProvider media, CancellationToken token)
    {
        foreach (var primary in folder.GetRecursiveChildren().OfType<Video>())
        {
            token.ThrowIfCancellationRequested();
            await RepairVideoAsync(primary, library, media, token).ConfigureAwait(false);
        }
    }

    public static async Task RepairVideoAsync(Video primary, ILibraryManager library, LibraryMediaProvider media, CancellationToken token)
    {
        var record = media.Record(primary);
        if (record is null || primary.PrimaryVersionId is not null || string.IsNullOrEmpty(primary.Path)) return;
        if (RepairShortcut(primary)) await library.UpdateItemAsync(primary, primary.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
        if (record.Season is not null && !Regex.IsMatch(Path.GetFileName(primary.Path), @" - voice-[0-9]{1,16}\.strm$", RegexOptions.CultureInvariant))
        {
            var changed = false;
            var nfoPath = Path.ChangeExtension(primary.Path, ".nfo");
            if (File.Exists(nfoPath))
            {
                var title = XDocument.Load(nfoPath).Root?.Element("title")?.Value;
                if (int.TryParse(record.Number, out var number) && EpisodeTitleSelection.ShouldRepair(primary.Name, primary.Path, title, number,
                    primary.IsLocked || primary.LockedFields.Contains(MetadataField.Name))) { primary.Name = title!; changed = true; }
            }
            var paths = EpisodeVersionPaths.Collect(primary.Path, record.Voices);
            if (!primary.LocalAlternateVersions.SequenceEqual(paths, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            { primary.LocalAlternateVersions = paths.ToArray(); changed = true; }
            if (changed) await library.UpdateItemAsync(primary, primary.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
            var linked = library.GetLocalAlternateVersionIds(primary).ToHashSet();
            foreach (var path in paths)
            {
                var alternate = library.GetItemById(library.GetNewItemId(path, primary.GetType())) as Video;
                if (alternate is null) continue;
                var repairedShortcut = RepairShortcut(alternate);
                var updateName = ShouldSyncAlternateName(alternate.Name, primary.Name, alternate.IsLocked || alternate.LockedFields.Contains(MetadataField.Name));
                if (alternate.OwnerId != primary.Id || alternate.PrimaryVersionId != primary.Id || updateName || repairedShortcut)
                {
                    alternate.OwnerId = primary.Id;
                    alternate.SetPrimaryVersionId(primary.Id);
                    if (updateName) alternate.Name = primary.Name;
                    await library.UpdateItemAsync(alternate, alternate.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
                }
                if (!linked.Contains(alternate.Id)) library.UpsertLinkedChild(primary.Id, alternate.Id, LinkedChildType.LocalAlternateVersion);
            }
        }
        foreach (var id in library.GetLocalAlternateVersionIds(primary))
        {
            if (library.GetItemById(id) is not Video alternate || media.Record(alternate) is null) continue;
            var changed = RepairShortcut(alternate);
            if (!alternate.IsLocked && !alternate.LockedFields.Contains(MetadataField.Name) && ShouldRepair(alternate.Name, alternate.Path, primary.Name)) { alternate.Name = primary.Name; changed = true; }
            if (changed) await library.UpdateItemAsync(alternate, alternate.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
        }
    }
}
