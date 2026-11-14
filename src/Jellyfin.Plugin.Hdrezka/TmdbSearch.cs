using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record TmdbCandidate(int Id, string Title, string OriginalTitle, string ReleaseDate, string? PosterPath);

public static class TmdbSearch
{
    private static readonly IReadOnlyDictionary<string, string> Transliteration = new Dictionary<string, string>
    {
        ["shch"]="щ", ["sch"]="щ", ["aya"]="ая", ["yya"]="ыя", ["zh"]="ж", ["kh"]="х", ["ts"]="ц", ["ch"]="ч", ["sh"]="ш", ["yu"]="ю", ["ya"]="я", ["yo"]="ё", ["yy"]="ый", ["iy"]="ий", ["oy"]="ой", ["ey"]="ей", ["a"]="а", ["b"]="б", ["v"]="в", ["g"]="г", ["d"]="д", ["e"]="е", ["z"]="з", ["i"]="и", ["y"]="й", ["k"]="к", ["l"]="л", ["m"]="м", ["n"]="н", ["o"]="о", ["p"]="п", ["r"]="р", ["s"]="с", ["t"]="т", ["u"]="у", ["f"]="ф", ["h"]="х", ["c"]="ц", ["j"]="ж"
    };

    public static string ToCyrillic(string value) => Regex.Replace(value.ToLowerInvariant(), @"shch|sch|aya|yya|zh|kh|ts|ch|sh|yu|ya|yo|yy|iy|oy|ey|[a-z]", m => Transliteration.TryGetValue(m.Value, out var replacement) ? replacement : m.Value);

    public static TmdbCandidate? Choose(string query, int? year, IEnumerable<TmdbCandidate> candidates)
    {
        var possible = new[] { Normalize(query), Normalize(ToCyrillic(query)) }.ToHashSet(StringComparer.Ordinal);
        var matches = candidates.Where(c => (possible.Contains(Normalize(c.Title)) || possible.Contains(Normalize(c.OriginalTitle)))
            && (!year.HasValue || (c.ReleaseDate.Length >= 4 && int.TryParse(c.ReleaseDate[..4], out var y) && y == year))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant().Replace('ё', 'е'), @"[^\p{L}\p{N}]+", "");
}
