using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Hdrezka;

public static class EpisodeScheduleParser
{
    public static IReadOnlyDictionary<(int Season, int Episode), string> Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new Dictionary<(int, int), string>();
        var blocks = doc.DocumentNode.SelectNodes("//div[contains(concat(' ',normalize-space(@class),' '),' b-post__schedule_block ')]");
        if (blocks is null) return result;
        foreach (var block in blocks)
        {
            var title = block.SelectSingleNode(".//div[contains(concat(' ',normalize-space(@class),' '),' b-post__schedule_block_title ')]//div[contains(concat(' ',normalize-space(@class),' '),' title ')]");
            if (!TryNumber(title?.InnerText, out var season)) continue;
            foreach (var row in block.SelectNodes(".//tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var number = row.SelectSingleNode(".//td[contains(concat(' ',normalize-space(@class),' '),' td-1 ')]");
                var name = row.SelectSingleNode(".//td[contains(concat(' ',normalize-space(@class),' '),' td-2 ')]");
                if (!TryEpisodeNumber(number?.InnerText, season, out var episode) || name is null) continue;
                var russian = Clean(name.SelectSingleNode(".//b")?.InnerText);
                var original = Clean(name.SelectSingleNode(".//span")?.InnerText);
                var selected = russian.Length > 0 ? russian : original;
                if (selected.Length > 0) result.TryAdd((season, episode), selected);
            }
        }
        return result;
    }

    public static IReadOnlyList<Episode> Apply(IReadOnlyList<Episode> episodes, IReadOnlyDictionary<(int Season, int Episode), string>? names)
        => names is null || names.Count == 0 ? episodes : episodes.Select(item =>
            int.TryParse(item.Season, NumberStyles.None, CultureInfo.InvariantCulture, out var season) &&
            int.TryParse(item.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && names.TryGetValue((season, number), out var title)
                ? item with { Title = title } : item).ToArray();

    private static bool TryNumber(string? input, out int number)
    {
        number = 0;
        var match = Regex.Match(input ?? "", @"(?<!\d)\d{1,4}(?!\d)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number > 0;
    }

    private static bool TryEpisodeNumber(string? text, int blockSeason, out int episode)
    {
        episode = 0;
        var numbers = Regex.Matches(text ?? "", @"(?<!\d)\d{1,4}(?!\d)", RegexOptions.CultureInvariant);
        if (numbers.Count is < 1 or > 2) return false;
        if (numbers.Count == 2 && (!int.TryParse(numbers[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var season) || season != blockSeason)) return false;
        return int.TryParse(numbers[^1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out episode) && episode > 0;
    }

    private static string Clean(string? value)
    {
        var text = Regex.Replace(HtmlEntity.DeEntitize(value ?? ""), @"\s+", " ").Trim();
        return text is "-" or "—" ? "" : text;
    }
}
