namespace Jellyfin.Plugin.Hdrezka;

public static class SitemapDetailRoute
{
    public static bool IsEpisodeList(string? method, string? controller, string? action) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && string.Equals(controller, "TvShows", StringComparison.OrdinalIgnoreCase)
        && string.Equals(action, "GetEpisodes", StringComparison.OrdinalIgnoreCase);

    public static bool IsDetail(string? method, string? controller, string? action) =>
        string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
        && string.Equals(controller, "UserLibrary", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(action, "GetItem", StringComparison.OrdinalIgnoreCase) || string.Equals(action, "GetItemLegacy", StringComparison.OrdinalIgnoreCase));
}
