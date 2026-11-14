namespace Jellyfin.Plugin.Hdrezka;

public static class CdnSelection
{
    public static readonly string[] Hosts = ["prx.ukrtelcdn.net", "prx-ams.ukrtelcdn.net", "prx2-ams.ukrtelcdn.net", "stream.voidboost.top", "stream.voidboost.link", "stream.voidboost.club", "stream.voidboost.cc"];
    private static readonly HashSet<string> Allowed = new(Hosts.Concat(["stream.voidboost.one", "ukrtelcdn.net"]), StringComparer.OrdinalIgnoreCase);
    public static Uri Apply(Uri source, string? host)
    {
        if (!Allowed.Contains(source.Host) || string.IsNullOrWhiteSpace(host) || !Hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) return source;
        return new UriBuilder(source) { Host = host }.Uri;
    }
}
