using System.Text.Json;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record LibraryMovieRecord(string Id, string Path, IReadOnlyList<FreeVoice> Voices);

public static class MovieLibraryWriter
{
    internal const string Placeholder = "http://127.0.0.1:9/hdrezka-placeholder";

    public static async Task WriteAsync(string root, CatalogEntry entry, CatalogDetail detail, IReadOnlyList<FreeVoice> voices, CancellationToken token)
    {
        var id = CatalogIdentity.FromPath(entry.Url.AbsolutePath) ?? throw new ArgumentException("Invalid video identity.", nameof(entry));
        if (detail.HasEpisodes) throw new ArgumentException("A series cannot be written as a movie.", nameof(detail));
        var dir = Path.Combine(Path.GetFullPath(root), LibraryPath.TitleDirectory(id));
        Directory.CreateDirectory(dir);
        var document = new XDocument(new XElement("movie", new XElement("title", detail.Title), new XElement("plot", detail.Overview), YearElement(entry.Description),
            entry.Poster is null ? null : new XElement("thumb", entry.Poster.AbsoluteUri)));
        await AtomicWriteAsync(Path.Combine(dir, "movie.nfo"), document.ToString(), token).ConfigureAwait(false);
        var record = new LibraryMovieRecord(id, entry.Url.AbsolutePath, voices);
        await AtomicWriteAsync(Path.Combine(dir, "hdrezka.json"), JsonSerializer.Serialize(record), token).ConfigureAwait(false);
        var primary = Path.Combine(dir, LibraryPath.PrimaryFileName(id));
        if (!File.Exists(primary)) await AtomicWriteAsync(primary, Placeholder, token).ConfigureAwait(false);
        foreach (var voice in voices.Skip(1))
        {
            var path = Path.Combine(dir, LibraryPath.VersionFileName(id, voice.Id));
            if (!File.Exists(path)) await AtomicWriteAsync(path, Placeholder, token).ConfigureAwait(false);
        }
    }

    internal static XElement? YearElement(string description)
    {
        var match = Regex.Match(description, @"^\s*((?:18|19|20)\d{2})(?!\d)", RegexOptions.CultureInvariant);
        return match.Success ? new XElement("year", match.Groups[1].Value) : null;
    }

    internal static async Task AtomicWriteAsync(string path, string contents, CancellationToken token)
    {
        if (File.Exists(path) && string.Equals(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), contents, StringComparison.Ordinal)) return;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, contents, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
