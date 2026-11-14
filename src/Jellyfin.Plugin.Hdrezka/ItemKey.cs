using System.Text;

namespace Jellyfin.Plugin.Hdrezka;

public static class ItemKey
{
    public static string EncodePath(string path)
    {
        if (!IsSafePath(path)) throw new ArgumentException("Expected a safe mirror-relative path", nameof(path));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(path)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static bool TryDecodePath(string encoded, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 4096) return false;
        try
        {
            var value = encoded.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value));
            if (!IsSafePath(decoded)) return false;
            path = decoded; return true;
        }
        catch (FormatException) { return false; }
    }
    private static bool IsSafePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal)) return false;
        try
        {
            var decoded = Uri.UnescapeDataString(path);
            return !decoded.Contains('\\') && !decoded.Contains('?') && !decoded.Contains('#') && !decoded.Any(char.IsControl)
                && !decoded.Split('/').Any(x => x is "." or "..") && !decoded.StartsWith("//", StringComparison.Ordinal);
        }
        catch (UriFormatException) { return false; }
    }
}
