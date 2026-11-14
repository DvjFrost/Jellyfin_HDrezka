using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class SeriesMigration(ILibraryManager library, IUserManager users, IUserDataManager data, LibraryMediaProvider media)
{
    public async Task<Guid> ConvertAsync(string root, Video movie, string stagedDirectory, string sourceId, CancellationToken token)
    {
        if (movie.PrimaryVersionId is not null) throw new InvalidOperationException("Expected a primary video.");
        while (library.IsScanRunning) await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
        var itemDirectory = Path.GetDirectoryName(movie.Path) ?? throw new InvalidOperationException("Missing movie directory.");
        var parent = movie.GetParent() as Folder ?? throw new InvalidOperationException("Missing movie parent.");
        if (string.Equals(parent.Path, itemDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            parent = parent.GetParent() as Folder ?? throw new InvalidOperationException("Missing library parent.");
        var savedUsers = users.GetUsers().Select(user => (User: user, Data: data.GetUserData(user, movie))).Where(x => x.Data is { } d &&
            (d.IsFavorite || d.Played || d.PlayCount > 0 || d.PlaybackPositionTicks > 0 || d.Rating.HasValue || d.LastPlayedDate.HasValue || d.AudioStreamIndex.HasValue || d.SubtitleStreamIndex.HasValue)).ToArray();
        var files = SeriesMigrationFiles.Begin(root, itemDirectory, stagedDirectory, sourceId);
        Series? series = null;
        var oldDeletionStarted = false;
        try
        {
            var hydratedName = System.Xml.Linq.XDocument.Load(Path.Combine(itemDirectory, "tvshow.nfo")).Root?.Element("title")?.Value;
            series = library.ResolvePath(BaseItem.FileSystem.GetDirectoryInfo(itemDirectory), parent, new DirectoryService(BaseItem.FileSystem)) as Series
                ?? throw new InvalidOperationException("Jellyfin did not recognize the staged series.");
            if (library.GetItemById(series.Id) is not null) throw new InvalidOperationException("The replacement series already exists.");
            library.CreateItem(series, parent);
            var options = new MetadataRefreshOptions(new DirectoryService(BaseItem.FileSystem)) { MetadataRefreshMode = MetadataRefreshMode.FullRefresh, EnableRemoteContentProbe = false, ForceSave = true };
            await series.RefreshMetadata(options, token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(hydratedName) && series.Name != hydratedName)
            {
                series.Name = hydratedName;
                await library.UpdateItemAsync(series, series.GetParent(), ItemUpdateType.MetadataEdit, token).ConfigureAwait(false);
            }
            await series.ValidateChildren(new Progress<double>(), options, cancellationToken: token).ConfigureAwait(false);
            await LibraryVersionTitles.RepairAsync(series, library, media, token).ConfigureAwait(false);
            if (library.IsScanRunning) throw new InvalidOperationException("Library scan started during series migration; the original will be restored.");
            foreach (var (user, value) in savedUsers)
                if (users.GetUserById(user.Id) is not null) data.SaveUserData(user, series, value!, UserDataSaveReason.Import, token);
            var map = Path.Combine(root, ".hdrezka-migrations", movie.Id.ToString("N") + ".map");
            await MovieLibraryWriter.AtomicWriteAsync(map, series.Id.ToString("N"), token).ConfigureAwait(false);
            oldDeletionStarted = true;
            library.DeleteItem(movie, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
            return series.Id;
        }
        catch
        {
            if (!oldDeletionStarted)
            {
                if (series is not null && library.GetItemById(series.Id) is not null)
                    library.DeleteItem(series, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
                files.Rollback();
            }
            throw;
        }
    }

    public static Guid? FindReplacement(string root, Guid oldId)
    {
        var path = Path.Combine(root, ".hdrezka-migrations", oldId.ToString("N") + ".map");
        return File.Exists(path) && Guid.TryParse(File.ReadAllText(path), out var id) ? id : null;
    }
}
