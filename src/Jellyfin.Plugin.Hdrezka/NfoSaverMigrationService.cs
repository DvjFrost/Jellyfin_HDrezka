using MediaBrowser.Controller;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

// Update only existing plugin-owned libraries, without requesting the mirror or
// starting a catalogue scan. New libraries are still created by the index task.
public sealed class NfoSaverMigrationService(IServerApplicationHost host, ILibraryManager library, LibraryMediaProvider media, ILogger<NfoSaverMigrationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!host.CoreStartupHasCompleted) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            foreach (var folder in library.GetVirtualFolders())
            {
                stoppingToken.ThrowIfCancellationRequested();
                var kind = SitemapLibraries.OwnedKind(folder.Name, folder.Locations, media.Root);
                if (kind is null || !Guid.TryParse(folder.ItemId, out var id) || library.GetItemById<CollectionFolder>(id) is not { } collection) continue;
                var options = collection.GetLibraryOptions();
                var before = System.Text.Json.JsonSerializer.Serialize(options);
                SitemapLibraries.ConfigureNfoSaver(options);
                if (before != System.Text.Json.JsonSerializer.Serialize(options)) collection.UpdateLibraryOptions(options);
            }
            logger.LogInformation("HDrezka NFO saver configuration verified");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError("Could not configure HDrezka NFO saving: {ErrorType}", ex.GetType().Name); }
    }
}
