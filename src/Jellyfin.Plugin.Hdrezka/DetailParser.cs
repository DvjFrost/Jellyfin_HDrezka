using HtmlAgilityPack;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record CatalogDetail(string Title, string Overview, bool HasEpisodes, Uri? Poster = null)
{
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Actors { get; init; } = [];
    public IReadOnlyList<string> Directors { get; init; } = [];
    public string? Tagline { get; init; }
    public double? ImdbRating { get; init; }
    public string? OriginalTitle { get; init; }
    public string? ReleaseDate { get; init; }
    public string? ImdbId { get; init; }
    public string? AgeRating { get; init; }
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
        var original = Text(doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-post__origtitle ')]"));
        var originalTitle = original.Split(" / ", StringSplitOptions.None)[0].Trim();
        var rows = doc.DocumentNode.SelectNodes("//table[contains(concat(' ',normalize-space(@class),' '),' b-post__info ')]//tr") ?? Enumerable.Empty<HtmlNode>();
        string RowValue(string label) => rows.Where(row => Text(row.SelectSingleNode(".//h2")).TrimEnd(':', ' ').Equals(label, StringComparison.OrdinalIgnoreCase))
            .Select(row => Text(row.SelectNodes("./td")?.LastOrDefault())).FirstOrDefault() ?? "";
        var dateMatch = Regex.Match(RowValue("Дата выхода"), @"\b(\d{1,2})\s+([а-яё]+)\s+((?:18|19|20)\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        string? releaseDate = null;
        if (dateMatch.Success && DateOnly.TryParseExact(dateMatch.Groups[1].Value + " " + dateMatch.Groups[2].Value + " " + dateMatch.Groups[3].Value, "d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var parsedDate))
            releaseDate = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var age = Regex.Match(RowValue("Возраст"), @"(?<!\d)(0|6|12|16|18)\s*\+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var imdbLink = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-post__info_rates ') and contains(concat(' ',normalize-space(@class),' '),' imdb ')]//a")?.GetAttributeValue("href", "");
        var imdbId = ParseImdbId(imdbLink, mirror);
        return new CatalogDetail(title, overview, hasEpisodes, poster) { Genres = genres, Actors = actors, Directors = directors, Tagline = tagline, ImdbRating = rating,
            OriginalTitle = originalTitle.Length > 0 ? originalTitle : null, ReleaseDate = releaseDate, ImdbId = imdbId, AgeRating = age.Success ? age.Groups[1].Value + "+" : null };
    }

    private static string? ParseImdbId(string? href, Uri? mirror)
    {
        if (string.IsNullOrWhiteSpace(href) || href.Length > 4096) return null;
        try
        {
            var value = href;
            if (Uri.TryCreate(value, UriKind.Absolute, out var redirect) && mirror is not null && redirect.Host.Equals(mirror.Host, StringComparison.OrdinalIgnoreCase)) value = redirect.PathAndQuery;
            if (value.StartsWith("/help/", StringComparison.OrdinalIgnoreCase))
            {
                var encoded = value[6..].Trim('/');
                if (encoded.Length > 2048 || encoded.Contains('/')) return null;
                value = Uri.UnescapeDataString(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            }
            if (!Uri.TryCreate(value, UriKind.Absolute, out var target) || target.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(target.UserInfo) || target.Port != 443 ||
                !(target.Host.Equals("imdb.com", StringComparison.OrdinalIgnoreCase) || target.Host.Equals("www.imdb.com", StringComparison.OrdinalIgnoreCase))) return null;
            var match = Regex.Match(target.AbsolutePath, @"^/title/(tt\d{5,12})(?:/|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }
        catch (Exception ex) when (ex is FormatException or UriFormatException or ArgumentException) { return null; }
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
