namespace Jellyfin.Plugin.Hdrezka;

public static class CatalogTitleRepair
{
    public static bool ShouldReplace(string? current, string? originalPlaceholder, string? corrected, bool locked) =>
        !locked && !string.IsNullOrWhiteSpace(corrected)
        && !string.IsNullOrWhiteSpace(originalPlaceholder)
        && string.Equals(current, originalPlaceholder, StringComparison.Ordinal)
        && !string.Equals(current, corrected, StringComparison.Ordinal);
}
