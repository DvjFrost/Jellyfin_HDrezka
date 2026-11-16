using HtmlAgilityPack;
using System.Text;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record CatalogDetail(string Title, string Overview, bool HasEpisodes, Uri? Poster = null)
{
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Actors { get; init; } = [];
    public IReadOnlyList<string> Directors { get; init; } = [];
    public string? Tagline { get; init; }
    public double? ImdbRating { get; init; }
}

public static class DetailParser
{
    public static CatalogDetail? Parse(string html, Uri? mirror = null)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var title = Text(doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ',normalize-space(@class),' '),' b-post__title ')]//h1"));
        if (title.Length == 0) return null;
        var overview = Text(doc.DocumentNode.SelectSingleNode("//div[contains(concat(' ',normalize-space(@class),' '),' b-post__description_text ')]"));
        var hasEpisodes = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-simple_seasons__list ') or contains(concat(' ',normalize-space(@class),' '),' b-simple_episodes__list ')]") is not null
            || html.Contains("initCDNSeriesEvents", StringComparison.OrdinalIgnoreCase);
        var image = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-sidecover ')]//img")?.GetAttributeValue("src", "")
            ?? doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "");
        Uri? poster = null;
        if (!string.IsNullOrWhiteSpace(image) && (Uri.TryCreate(image, UriKind.Absolute, out var absolute) || (mirror is not null && Uri.TryCreate(mirror, image, out absolute))))
        {
            var host = absolute!.IdnHost;
            var trusted = (mirror is not null && host.Equals(mirror.IdnHost, StringComparison.OrdinalIgnoreCase))
                || host.Equals("hdrezka.ac", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".hdrezka.ac", StringComparison.OrdinalIgnoreCase)
                || host.Equals("rezka.ag", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".rezka.ag", StringComparison.OrdinalIgnoreCase)
                || host.Equals("hdrezka.ag", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".hdrezka.ag", StringComparison.OrdinalIgnoreCase);
            if (trusted && absolute.Scheme == Uri.UriSchemeHttps && absolute.Port == 443 && string.IsNullOrEmpty(absolute.UserInfo)) poster = absolute;
        }
        var genres = (doc.DocumentNode.SelectNodes("//*[@itemprop='genre']") ?? Enumerable.Empty<HtmlNode>()).Select(Text).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var actors = (doc.DocumentNode.SelectNodes("//*[@itemprop='actor']//*[@itemprop='name']") ?? Enumerable.Empty<HtmlNode>()).Select(Text).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var directors = (doc.DocumentNode.SelectNodes("//*[@itemprop='director']//*[@itemprop='name']") ?? Enumerable.Empty<HtmlNode>()).Select(Text).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var tagline = (doc.DocumentNode.SelectNodes("//table[contains(concat(' ',normalize-space(@class),' '),' b-post__info ')]//tr") ?? Enumerable.Empty<HtmlNode>())
            .Where(row => Text(row.SelectSingleNode(".//h2")).Equals("Слоган", StringComparison.OrdinalIgnoreCase))
            .Select(row => Text(row.SelectNodes("./td")?.LastOrDefault())).FirstOrDefault();
        var imdb = Text(doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-post__info_rates ') and contains(concat(' ',normalize-space(@class),' '),' imdb ')]//*[contains(concat(' ',normalize-space(@class),' '),' bold ')]"));
        double? rating = double.TryParse(imdb.Replace(',', '.'), System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed is >= 0 and <= 10 ? parsed : null;
        return new CatalogDetail(title, overview, hasEpisodes, poster) { Genres = genres, Actors = actors, Directors = directors, Tagline = tagline, ImdbRating = rating };
    }

    private static string Text(HtmlNode? node)
    {
        if (node is null) return "";
        var text = new StringBuilder();
        Append(node, text);
        return HtmlEntity.DeEntitize(text.ToString()).Trim();
    }

    private static void Append(HtmlNode node, StringBuilder output)
    {
        if (node.NodeType == HtmlNodeType.Text) { output.Append(node.InnerText); return; }
        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase)) { output.Append('\n'); return; }
        if (node.Name.Equals("script", StringComparison.OrdinalIgnoreCase) || node.Name.Equals("style", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var child in node.ChildNodes) Append(child, output);
        if (node.Name.Equals("p", StringComparison.OrdinalIgnoreCase)) output.Append('\n');
    }
}
