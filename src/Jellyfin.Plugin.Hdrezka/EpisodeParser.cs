using HtmlAgilityPack;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record Episode(string Season, string Number, string Title);
public static class EpisodeParser
{
    public static IReadOnlyList<Episode> Parse(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var lists = doc.DocumentNode.SelectNodes("//ul[contains(concat(' ',normalize-space(@class),' '),' b-simple_episodes__list ')]");
        if (lists is null) return Array.Empty<Episode>();
        var result = new List<Episode>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            const string prefix = "simple-episodes-list-";
            var id = list.GetAttributeValue("id", "");
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var season = id[prefix.Length..];
            if (season.Length is < 1 or > 4 || !season.All(char.IsAsciiDigit)) continue;
            var episodes = list.SelectNodes(".//li[contains(concat(' ',normalize-space(@class),' '),' b-simple_episode__item ')]");
            if (episodes is null) continue;
            foreach (var item in episodes)
            {
                var number = item.GetAttributeValue("data-episode_id", "");
                if (number.Length is < 1 or > 4 || !number.All(char.IsAsciiDigit) || !seen.Add(season + ":" + number)) continue;
                var title = HtmlEntity.DeEntitize(item.InnerText).Trim();
                result.Add(new Episode(season, number, title));
            }
        }
        return result;
    }
}
