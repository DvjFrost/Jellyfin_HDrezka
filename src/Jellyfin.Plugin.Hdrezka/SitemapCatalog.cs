using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record SitemapVideo(string Id, string Query, int? Year, CatalogKind Kind, Uri Url);

public static class SitemapCatalog
{
    public static IReadOnlyList<Uri> ParseIndex(string xml, Uri mirror)
    {
        if (!mirror.IsAbsoluteUri || mirror.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(mirror.UserInfo)) throw new ArgumentException("Invalid mirror", nameof(mirror));
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name.LocalName != "sitemapindex") throw new XmlException("Expected sitemap index");
        var urls = new List<Uri>();
        foreach (var item in root.Elements().Where(x => x.Name.LocalName == "sitemap"))
        {
            var loc = item.Elements().FirstOrDefault(x => x.Name.LocalName == "loc")?.Value;
            if (!Uri.TryCreate(loc, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) continue;
            if (!Regex.IsMatch(uri.AbsolutePath, @"^/(?:uploads/)?sitemap[0-9]{1,4}\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
            var target = new Uri(new Uri(mirror.GetLeftPart(UriPartial.Authority)), uri.AbsolutePath);
            if (!urls.Contains(target)) urls.Add(target);
        }
        return urls;
    }

    public static IReadOnlyList<SitemapVideo> Parse(string xml, Uri mirror)
    {
        if (!mirror.IsAbsoluteUri || mirror.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(mirror.UserInfo)) throw new ArgumentException("Invalid mirror", nameof(mirror));
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20_000_000 });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "urlset") throw new XmlException("Expected sitemap urlset");
        var results = new List<SitemapVideo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var url in document.Root.Elements().Where(x => x.Name.LocalName == "url"))
        {
            var loc = url.Elements().FirstOrDefault(x => x.Name.LocalName == "loc")?.Value;
            if (!Uri.TryCreate(loc, UriKind.Absolute, out var source) || source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(source.Query) || !string.IsNullOrEmpty(source.Fragment)) continue;
            var m = Regex.Match(source.AbsolutePath, @"^/(films|series|cartoons|animation|show)/[a-z0-9-]+/([0-9]{1,12})-([a-z0-9-]+)\.html$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success || !seen.Add(m.Groups[2].Value)) continue;
            var slug = m.Groups[3].Value;
            if (slug.EndsWith("-latest", StringComparison.OrdinalIgnoreCase)) slug = slug[..^7];
            var yearMatch = Regex.Match(slug, @"-(18[0-9]{2}|19[0-9]{2}|20[0-9]{2})$");
            int? year = yearMatch.Success ? int.Parse(yearMatch.Groups[1].Value) : null;
            if (yearMatch.Success) slug = slug[..yearMatch.Index];
            var kind = m.Groups[1].Value.ToLowerInvariant() switch { "films" => CatalogKind.Movie, "series" => CatalogKind.Series, "animation" => CatalogKind.Anime, "show" => CatalogKind.Show, _ => CatalogKind.Cartoon };
            results.Add(new SitemapVideo(m.Groups[2].Value, slug.Replace('-', ' '), year, kind, new Uri(new Uri(mirror.GetLeftPart(UriPartial.Authority)), source.AbsolutePath)));
        }
        return results;
    }
}
