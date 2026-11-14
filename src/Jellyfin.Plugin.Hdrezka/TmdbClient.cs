using System.Net.Http.Headers;
using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class TmdbClient(HttpClient http, string token)
{
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
