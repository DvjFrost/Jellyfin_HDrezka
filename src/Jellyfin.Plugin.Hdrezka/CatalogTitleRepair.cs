using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public static class CatalogTitleRepair
{
    public static bool IsSitePlaceholder(string? current, string? path)
    {
        if (string.IsNullOrWhiteSpace(current)) return true;
        if (string.IsNullOrWhiteSpace(path)) return false;
        var match = Regex.Match(path, @"/(?<id>[0-9]{1,16})-(?<slug>[a-z0-9-]+)\.html$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) return false;
        if (string.Equals(current, "HDrezka-" + match.Groups["id"].Value, StringComparison.OrdinalIgnoreCase)) return true;
        var slug = match.Groups["slug"].Value;
        if (slug.EndsWith("-latest", StringComparison.OrdinalIgnoreCase)) slug = slug[..^7];
        slug = Regex.Replace(slug, @"-(?:18|19|20)[0-9]{2}$", "", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return string.Equals(current, slug.Replace('-', ' '), StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldReplace(string? current, string? originalPlaceholder, string? corrected, bool locked) =>
        !locked && !string.IsNullOrWhiteSpace(corrected)
        && !string.IsNullOrWhiteSpace(originalPlaceholder)
        && string.Equals(current, originalPlaceholder, StringComparison.Ordinal)
        && !string.Equals(current, corrected, StringComparison.Ordinal);
}
