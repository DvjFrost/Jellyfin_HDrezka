using System.Net.Http.Headers;
using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record TmdbActor(string Name, string Role);
public sealed record TmdbDetails(int Id, string OriginalTitle, string Overview, string Tagline, double? TmdbRating, IReadOnlyList<string> Genres, IReadOnlyList<TmdbActor> Actors, IReadOnlyList<string> Directors, string? PosterPath, string? BackdropPath)
{
    public string? ReleaseDate { get; init; }
    public string? OriginalLanguage { get; init; }
    public string? ImdbId { get; init; }
    public IReadOnlyList<string> Studios { get; init; } = [];
    public IReadOnlyList<string> Countries { get; init; } = [];
}

public sealed class TmdbClient(HttpClient http, string token)
{
    public async Task<TmdbDetails> DetailsAsync(int id, bool series, CancellationToken cancellationToken)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("TMDB token is not configured.");
        using var req = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://api.themoviedb.org/3/{(series ? "tv" : "movie")}/{id}?language=ru-RU&append_to_response=credits,external_ids"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var receivedId) || receivedId != id) throw new JsonException("TMDB detail identity mismatch.");
        static string Get(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        static IEnumerable<JsonElement> Array(JsonElement node, string name) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array ? p.EnumerateArray() : Enumerable.Empty<JsonElement>();
        var credits = root.TryGetProperty("credits", out var c) ? c : default;
        var genres = Array(root, "genres").Select(x => Get(x, "name")).Where(x => x.Length > 0).Distinct().ToArray();
        var actors = Array(credits, "cast").Select(x => new TmdbActor(Get(x, "name"), Get(x, "character"))).Where(x => x.Name.Length > 0).Take(50).ToArray();
        var directors = Array(credits, "crew").Where(x => Get(x, "job") == "Director").Select(x => Get(x, "name")).Where(x => x.Length > 0).Distinct().ToArray();
        var rating = root.TryGetProperty("vote_average", out var vote) && vote.TryGetDouble(out var v) && v is > 0 and <= 10 ? v : (double?)null;
        var external = root.TryGetProperty("external_ids", out var ids) ? ids : default;
        var imdb = Get(root, "imdb_id"); if (imdb.Length == 0) imdb = Get(external, "imdb_id");
        var studios = Array(root, "production_companies").Select(x => Get(x, "name")).Where(x => x.Length > 0).Distinct().ToArray();
        var countries = Array(root, "production_countries").Select(x => Get(x, "name")).Where(x => x.Length > 0).Distinct().ToArray();
        return new(id, Get(root, series ? "original_name" : "original_title"), Get(root, "overview"), Get(root, "tagline"), rating, genres, actors, directors, Get(root, "poster_path"), Get(root, "backdrop_path"))
        { ReleaseDate = Get(root, series ? "first_air_date" : "release_date"), OriginalLanguage = Get(root, "original_language"), ImdbId = imdb, Studios = studios, Countries = countries };
    }
    public async Task<IReadOnlyList<TmdbCandidate>> SearchAsync(string query, bool series, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("TMDB token is not configured.");
        if (string.IsNullOrWhiteSpace(query)) return [];
        var path = series ? "tv" : "movie";
        var uri = new Uri("https://api.themoviedb.org/3/search/" + path + "?language=ru-RU&query=" + Uri.EscapeDataString(query));
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid TMDB response.");
        var found = new List<TmdbCandidate>();
        foreach (var e in results.EnumerateArray())
        {
            if (!e.TryGetProperty("id", out var id) || !id.TryGetInt32(out var value) || value <= 0) continue;
            string Get(string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
            found.Add(new TmdbCandidate(value, Get(series ? "name" : "title"), Get(series ? "original_name" : "original_title"), Get(series ? "first_air_date" : "release_date"), Get("poster_path")));
        }
        return found;
    }
}
