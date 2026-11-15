using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record VideoStream(int Quality, Uri Url);

public static partial class StreamParser
{
    public static IReadOnlyList<VideoStream> Parse(string value) => ParseCore(value, false);

    public static IReadOnlyList<VideoStream> ParseAlternatives(string value) => ParseCore(value, true);

    private static IReadOnlyList<VideoStream> ParseCore(string value, bool allAlternatives)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1_000_000) return Array.Empty<VideoStream>();
        if (value.StartsWith("#h", StringComparison.Ordinal))
        {
            var encoded = value[2..];
            for (var i = 0; i < 20; i++)
            {
                var index = encoded.IndexOf("//_//", StringComparison.Ordinal);
                if (index < 0) break;
                if (index + 21 > encoded.Length) return Array.Empty<VideoStream>();
                encoded = encoded.Remove(index, 21);
            }
            try { value = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)); }
            catch (FormatException) { return Array.Empty<VideoStream>(); }
        }
        var result = new List<VideoStream>();
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var end = part.IndexOf(']');
            if (!part.StartsWith('[') || end < 2) continue;
            var label = HtmlEntity.DeEntitize(Regex.Replace(part[1..end], "<[^>]*>", ""));
            if (label.Contains("ultra", StringComparison.OrdinalIgnoreCase) || label.Contains("ультра", StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(label, @"(?<!\d)(\d{3,4})p?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var quality)) continue;
            var candidates = part[(end + 1)..].Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var candidate in allAlternatives ? candidates : candidates.TakeLast(1))
            {
                if (!Uri.TryCreate(HtmlEntity.DeEntitize(candidate), UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || !string.IsNullOrEmpty(uri.UserInfo)) continue;
                result.Add(new VideoStream(quality, uri));
            }
        }
        return result;
    }
}

