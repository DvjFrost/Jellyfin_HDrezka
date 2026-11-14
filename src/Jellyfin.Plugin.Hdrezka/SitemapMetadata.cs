using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record SitemapMetadata(string Id, bool FromTmdb, bool SiteMetadataLoaded);

public static class SitemapMetadataStore
{
    public static SitemapMetadata? Read(string directory, string id)
    {
        var path = Path.Combine(directory, "hdrezka-catalog.json");
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<SitemapMetadata>(File.ReadAllText(path));
        if (state is null || state.Id != id) throw new InvalidDataException("Invalid catalogue metadata identity.");
        return state;
    }

    public static Task WriteAsync(string directory, SitemapMetadata state, CancellationToken token)
        => MovieLibraryWriter.AtomicWriteAsync(Path.Combine(directory, "hdrezka-catalog.json"), JsonSerializer.Serialize(state), token);
}
