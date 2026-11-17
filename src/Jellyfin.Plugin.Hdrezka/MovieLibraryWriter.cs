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
        var nfoPath = Path.Combine(dir, "movie.nfo");
        await MetadataFileLock.RunAsync(nfoPath, async () =>
        {
            var document = MergeNfo(nfoPath, "movie", entry, detail);
            await AtomicWriteAsync(nfoPath, document.ToString(), token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
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

    internal static XDocument MergeNfo(string path, string rootName, CatalogEntry entry, CatalogDetail detail)
    {
        XDocument document;
        if (File.Exists(path))
        {
            document = XDocument.Load(path);
            if (document.Root?.Name != rootName) throw new InvalidDataException("Unexpected NFO root element.");
        }
        else document = new XDocument(new XElement(rootName));
        var root = document.Root!;
        var recognized = root.Elements().Any(e => e.Name.LocalName is "tmdbid" or "imdbid" or "tvdbid" or "uniqueid") ||
            string.Equals(root.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        void Set(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var existing = root.Element(name);
            if (existing is null) root.Add(new XElement(name, value));
            else if (!recognized || string.IsNullOrWhiteSpace(existing.Value) ||
                (name == "title" && CatalogTitleRepair.IsSitePlaceholder(existing.Value, entry.Url.AbsolutePath) &&
                 !string.Equals(root.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase))) existing.Value = value;
        }
        Set("title", detail.Title); Set("plot", detail.Overview);
        Set("year", YearElement(entry.Description)?.Value); Set("thumb", entry.Poster?.AbsoluteUri);
        Set("tagline", detail.Tagline);
        if (!root.Elements("genre").Any()) foreach (var genre in detail.Genres.Where(x => !string.IsNullOrWhiteSpace(x))) root.Add(new XElement("genre", genre));
        if (!root.Elements("director").Any()) foreach (var director in detail.Directors.Where(x => !string.IsNullOrWhiteSpace(x))) root.Add(new XElement("director", director));
        if (!root.Elements("actor").Any()) foreach (var actor in detail.Actors.Where(x => !string.IsNullOrWhiteSpace(x))) root.Add(new XElement("actor", new XElement("name", actor)));
        if (detail.ImdbRating is { } rating)
        {
            var ratings = root.Element("ratings");
            if (ratings is null) { ratings = new XElement("ratings"); root.Add(ratings); }
            if (!ratings.Elements("rating").Any(r => string.Equals((string?)r.Attribute("name"), "imdb", StringComparison.OrdinalIgnoreCase)))
                ratings.Add(new XElement("rating", new XAttribute("name", "imdb"), new XAttribute("max", "10"), new XElement("value", rating.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        return document;
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
