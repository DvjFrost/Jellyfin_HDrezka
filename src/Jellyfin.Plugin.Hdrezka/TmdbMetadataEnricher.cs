using System.Globalization;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public static class TmdbMetadataEnricher
{
    public static async Task<bool> ProcessAsync(string nfoPath, int matchedId, bool series, bool fromTmdb, bool itemLocked, TmdbClient client, CancellationToken token)
    {
        if (matchedId <= 0 || itemLocked || !File.Exists(nfoPath)) return false;
        var marker = Path.Combine(Path.GetDirectoryName(nfoPath)!, "hdrezka-tmdb-details.txt");
        var identity = matchedId.ToString(CultureInfo.InvariantCulture);
        var shouldFetch = false;
        await MetadataFileLock.RunAsync(nfoPath, async () =>
        {
            var doc = XDocument.Load(nfoPath);
            var root = doc.Root ?? throw new InvalidDataException("Missing NFO root.");
            var locked = string.Equals(root.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            var stored = root.Element("tmdbid")?.Value;
            if (!locked && string.IsNullOrWhiteSpace(stored) && fromTmdb && !root.Elements().Any(e => e.Name.LocalName is "imdbid" or "tvdbid" or "uniqueid"))
            {
                root.Add(new XElement("tmdbid", identity));
                await MovieLibraryWriter.AtomicWriteAsync(nfoPath, doc.ToString(), token).ConfigureAwait(false);
                stored = identity;
            }
            var completed = File.Exists(marker) ? await File.ReadAllTextAsync(marker, token).ConfigureAwait(false) : null;
            shouldFetch = TmdbEnrichmentRules.ShouldFetch(matchedId, stored, completed, locked);
        }, token).ConfigureAwait(false);
        if (!shouldFetch) return false;
        await TmdbMetadataWriter.EnrichAsync(nfoPath, matchedId, series, client, token).ConfigureAwait(false);
        var marked = false;
        await MetadataFileLock.RunAsync(nfoPath, async () =>
        {
            var latest = XDocument.Load(nfoPath).Root;
            if (latest is null || string.Equals(latest.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(latest.Element("tmdbid")?.Value, out var currentId) || currentId != matchedId) return;
            await MovieLibraryWriter.AtomicWriteAsync(Path.Combine(Path.GetDirectoryName(nfoPath)!, "hdrezka-tmdb-refresh-pending.txt"), identity, token).ConfigureAwait(false);
            await MovieLibraryWriter.AtomicWriteAsync(marker, identity, token).ConfigureAwait(false);
            marked = true;
        }, token).ConfigureAwait(false);
        return marked;
    }
}
