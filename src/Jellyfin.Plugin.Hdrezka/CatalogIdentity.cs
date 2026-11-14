using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public static class CatalogIdentity
{
    public static string? FromPath(string path)
    {
        try { _ = ItemKey.EncodePath(path); }
        catch (ArgumentException) { return null; }
        var match = Regex.Match(path, @"^/(?:films|series|cartoons|animation|show)/[^/]+/([1-9][0-9]{0,15})-[^/]+\.html$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }
}
