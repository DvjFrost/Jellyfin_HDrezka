using System.Globalization;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public static class TmdbMetadataWriter
{
    public static async Task EnrichAsync(string path, int tmdbId, bool series, TmdbClient client, CancellationToken token)
    {
        var document = XDocument.Load(path);
        var storedId = document.Root?.Element("tmdbid")?.Value;
        if (!int.TryParse(storedId, out var id) || id != tmdbId) throw new InvalidOperationException("TMDB identifier is missing or differs from the confirmed match.");
        if (string.Equals(document.Root?.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase)) return;
        var details = await client.DetailsAsync(id, series, token).ConfigureAwait(false);
        await MetadataFileLock.RunAsync(path, async () =>
        {
            var latest = XDocument.Load(path);
            if (!int.TryParse(latest.Root?.Element("tmdbid")?.Value, out var latestId) || latestId != id)
                throw new InvalidOperationException("TMDB identifier changed while metadata was loading.");
            Merge(latest, details);
            await MovieLibraryWriter.AtomicWriteAsync(path, latest.ToString(), token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
    }

    public static void Merge(XDocument document, TmdbDetails details)
    {
        var root = document.Root ?? throw new InvalidDataException("Missing NFO root.");
        if (root.Name != "movie" && root.Name != "tvshow") throw new InvalidDataException("Invalid NFO root.");
        if (string.Equals(root.Element("lockdata")?.Value, "true", StringComparison.OrdinalIgnoreCase)) return;
        var existingId = root.Element("tmdbid")?.Value;
        if (!string.IsNullOrWhiteSpace(existingId) && (!int.TryParse(existingId, out var id) || id != details.Id))
            throw new InvalidOperationException("TMDB identifier differs from the existing metadata.");
        void Fill(string name, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            var existing = root.Element(name);
            if (existing is null) root.Add(new XElement(name, value));
            else if (string.IsNullOrWhiteSpace(existing.Value)) existing.Value = value;
        }
        Fill("tmdbid", details.Id.ToString(CultureInfo.InvariantCulture));
        Fill("originaltitle", details.OriginalTitle); Fill("plot", details.Overview); Fill("tagline", details.Tagline);
        Fill("premiered", details.ReleaseDate); Fill("originallanguage", details.OriginalLanguage); Fill("imdbid", details.ImdbId);
        static string? Image(string? path) => path is not null && System.Text.RegularExpressions.Regex.IsMatch(path, @"^/[a-zA-Z0-9_-]+\.(jpg|png|webp)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)) ? "https://image.tmdb.org/t/p/original" + path : null;
        Fill("thumb", Image(details.PosterPath));
        var background = Image(details.BackdropPath);
        if (background is not null && root.Element("fanart") is null) root.Add(new XElement("fanart", new XElement("thumb", background)));
        if (!root.Elements("genre").Any()) foreach (var genre in details.Genres) root.Add(new XElement("genre", genre));
        if (!root.Elements("director").Any()) foreach (var director in details.Directors) root.Add(new XElement("director", director));
        if (!root.Elements("studio").Any()) foreach (var studio in details.Studios) root.Add(new XElement("studio", studio));
        if (!root.Elements("country").Any()) foreach (var country in details.Countries) root.Add(new XElement("country", country));
        if (!root.Elements("actor").Any()) foreach (var actor in details.Actors) root.Add(new XElement("actor", new XElement("name", actor.Name), new XElement("role", actor.Role)));
        if (details.TmdbRating is { } value)
        {
            var ratings = root.Element("ratings");
            if (ratings is null) { ratings = new XElement("ratings"); root.Add(ratings); }
            if (!ratings.Elements("rating").Any(x => string.Equals((string?)x.Attribute("name"), "tmdb", StringComparison.OrdinalIgnoreCase)))
                ratings.Add(new XElement("rating", new XAttribute("name", "tmdb"), new XAttribute("max", "10"), new XElement("value", value.ToString(CultureInfo.InvariantCulture))));
        }
    }
}
