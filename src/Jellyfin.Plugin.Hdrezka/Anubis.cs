using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record AnubisChallenge(string Id, string RandomData, int Difficulty, string Prefix);
public sealed record AnubisProof(long Nonce, string Hash);

public static class Anubis
{
    public static AnubisChallenge? TryParse(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 2_000_000) return null;
        try
        {
            var doc = new HtmlDocument(); doc.LoadHtml(html);
            var node = doc.GetElementbyId("anubis_challenge");
            if (node is null || !string.Equals(node.Name, "script", StringComparison.OrdinalIgnoreCase)) return null;
            using var json = JsonDocument.Parse(node.InnerHtml);
            var root = json.RootElement;
            if (!root.TryGetProperty("challenge", out var challenge) || !root.TryGetProperty("rules", out var rules)) return null;
            var algorithm = rules.TryGetProperty("algorithm", out var a) ? a.GetString() : null;
            if (!string.Equals(algorithm, "fast", StringComparison.OrdinalIgnoreCase)) return null;
            if (!rules.TryGetProperty("difficulty", out var d) || !d.TryGetInt32(out var difficulty) || difficulty is < 0 or > 7) return null;
            var id = challenge.GetProperty("id").GetString();
            var random = challenge.GetProperty("randomData").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 || string.IsNullOrWhiteSpace(random) || random.Length > 8192) return null;
            var prefixNode = doc.GetElementbyId("anubis_base_prefix");
            var prefix = prefixNode is null ? "" : JsonSerializer.Deserialize<string>(prefixNode.InnerHtml) ?? "";
            if (prefix.Length > 0 && (!prefix.StartsWith('/') || prefix.StartsWith("//", StringComparison.Ordinal)
                || !Regex.IsMatch(prefix, @"^/[A-Za-z0-9/_~.\-]*$", RegexOptions.CultureInvariant)
                || prefix.Split('/').Any(x => x is "." or ".."))) return null;
            return new AnubisChallenge(id, random, difficulty, prefix.TrimEnd('/'));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return null; }
    }

    public static AnubisProof? Solve(AnubisChallenge challenge, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        cancellationToken.ThrowIfCancellationRequested();
        if (challenge.Difficulty is < 0 or > 7 || challenge.RandomData.Length is 0 or > 8192 || timeout <= TimeSpan.Zero) return null;
        var max = timeout > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : timeout;
        var watch = Stopwatch.StartNew();
        for (long nonce = 0; nonce < 50_000_000; nonce++)
        {
            if ((nonce & 1023) == 0) { cancellationToken.ThrowIfCancellationRequested(); if (watch.Elapsed >= max) return null; }
            var input = Encoding.UTF8.GetBytes(challenge.RandomData + nonce.ToString(CultureInfo.InvariantCulture));
            var hash = SHA256.HashData(input);
            var valid = true;
            for (var i = 0; i < challenge.Difficulty; i++)
            {
                var nibble = i % 2 == 0 ? hash[i / 2] >> 4 : hash[i / 2] & 15;
                if (nibble != 0) { valid = false; break; }
            }
            if (valid) return new AnubisProof(nonce, Convert.ToHexStringLower(hash));
        }
        return null;
    }
}
