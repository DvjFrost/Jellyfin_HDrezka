using System.Globalization;

namespace Jellyfin.Plugin.Hdrezka;

public static class TmdbEnrichmentRules
{
    public static bool ShouldFetch(int? matchedId, string? storedId, string? marker, bool locked)
        => !locked && matchedId is > 0 && int.TryParse(storedId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed == matchedId.Value &&
           (!int.TryParse(marker?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var completedId) || completedId != matchedId.Value);
}
