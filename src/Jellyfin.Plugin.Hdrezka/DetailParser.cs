using HtmlAgilityPack;
using System.Text;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record CatalogDetail(string Title, string Overview, bool HasEpisodes, Uri? Poster = null);

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
        return new CatalogDetail(title, overview, hasEpisodes, poster);
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
