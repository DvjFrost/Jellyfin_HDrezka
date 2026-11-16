using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record FreeVoice(string Id, string ContentId, string Title, string IsCamrip, string IsAds, string IsDirector);

public static class VoiceParser
{
    public static FreeVoice? ParseSingleSeriesCandidate(string html, string? expectedContentId)
    {
        if (string.IsNullOrEmpty(expectedContentId) || expectedContentId.Length > 16 || !expectedContentId.All(char.IsAsciiDigit)) return null;
        var document = new HtmlDocument(); document.LoadHtml(html);
        if (document.DocumentNode.SelectSingleNode("//*[@data-translator_id or @data-translator-id]") is not null) return null;
        const string pattern = @"(?:sof\.tv\.)?initCDNSeriesEvents\s*\(\s*(?<content>[0-9]{1,16})\s*,\s*(?<voice>[0-9]{1,8})\s*,\s*[0-9]{1,4}\s*,\s*[0-9]{1,4}\s*,";
        foreach (var script in document.DocumentNode.SelectNodes("//script") ?? Enumerable.Empty<HtmlNode>())
        {
            var match = Regex.Match(script.InnerText, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (match.Success && match.Groups["content"].Value == expectedContentId)
                return new FreeVoice(match.Groups["voice"].Value, expectedContentId, SingleTranslationTitle(document) ?? "Доступная озвучка", "0", "0", "0");
        }
        return null;
    }

    public static IReadOnlyList<FreeVoice> ParseFree(string html, string? expectedContentId = null)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var nodes = document.DocumentNode.SelectNodes("//*[@data-translator_id or @data-translator-id]");
        if (nodes is null) return ParseSingleMovieVoice(document, expectedContentId);
        var result = new List<FreeVoice>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var classes = node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (classes.Any(x => x.Equals("b-prem_translator", StringComparison.OrdinalIgnoreCase))) continue;
            if (new[] { "data-premium", "data-is-premium", "data-paid", "data-is_paid" }.Any(x => node.GetAttributeValue(x, "") is "1" or "true")) continue;
            var id = node.GetAttributeValue("data-translator_id", node.GetAttributeValue("data-translator-id", ""));
            if (id.Length is < 1 or > 16 || !id.All(char.IsAsciiDigit) || !seen.Add(id)) continue;
            var contentId = node.GetAttributeValue("data-id", "");
            if (contentId.Length > 16 || !contentId.All(char.IsAsciiDigit)) continue;
            var title = HtmlEntity.DeEntitize(node.GetAttributeValue("title", "")).Trim();
            if (title.Length == 0) title = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (title.Length == 0) continue;
            static string Flag(HtmlNode n, string key) => n.GetAttributeValue(key, "0") == "1" ? "1" : "0";
            result.Add(new FreeVoice(id, contentId, title, Flag(node, "data-camrip"), Flag(node, "data-ads"), Flag(node, "data-director")));
        }
        return result;
    }

    private static IReadOnlyList<FreeVoice> ParseSingleMovieVoice(HtmlDocument document, string? expectedContentId)
    {
        if (string.IsNullOrEmpty(expectedContentId) || expectedContentId.Length > 16 || !expectedContentId.All(char.IsAsciiDigit)) return [];
        const string pattern = @"sof\.tv\.initCDNMoviesEvents\s*\(\s*(?<content>[0-9]{1,16})\s*,\s*(?<voice>[0-9]{1,16})\s*,\s*(?<cam>[01])\s*,\s*(?<ads>[01])\s*,\s*(?<director>[01])\s*,\s*['""'][^'""']+['""']\s*,\s*false\s*,\s*false\s*,\s*\{\s*""id""\s*:\s*""cdnplayer""\s*,\s*""streams""\s*:\s*""[^""\s]";
        foreach (var script in document.DocumentNode.SelectNodes("//script") ?? Enumerable.Empty<HtmlNode>())
        {
            var match = Regex.Match(script.InnerText, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            if (!match.Success || match.Groups["content"].Value != expectedContentId) continue;
            var voice = match.Groups["voice"].Value;
            var title = SingleTranslationTitle(document) ?? (voice switch { "56" => "Дубляж", "59" => "Многоголосый закадровый", "62" => "Одноголосый закадровый", "238" => "Оригинал (+субтитры)", _ => "Доступная озвучка" });
            return [new FreeVoice(voice, expectedContentId, title, match.Groups["cam"].Value, match.Groups["ads"].Value, match.Groups["director"].Value)];
        }
        return [];
    }

    private static string? SingleTranslationTitle(HtmlDocument document)
    {
        var rows = document.DocumentNode.SelectNodes("//table[contains(concat(' ', normalize-space(@class), ' '), ' b-post__info ')]//tr") ?? Enumerable.Empty<HtmlNode>();
        foreach (var row in rows)
        {
            var heading = row.SelectSingleNode(".//h2");
            if (heading is null || !HtmlEntity.DeEntitize(heading.InnerText).Trim().Equals("В переводе", StringComparison.OrdinalIgnoreCase)) continue;
            var cells = row.SelectNodes("./td");
            if (cells is null || cells.Count != 2) return null;
            var title = Regex.Replace(HtmlEntity.DeEntitize(cells[1].InnerText), @"\s+", " ").Trim();
            if (title.Length is 0 or > 160 || title.Contains(',') || title.Contains(';') || Regex.IsMatch(title, @"\s+и\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return null;
            return title;
        }
        return null;
    }
}
