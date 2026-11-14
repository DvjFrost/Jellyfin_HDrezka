using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public static class SingleVoiceDetails
{
    public static string Format(string voice, string? sourceName, string? overview)
    {
        var match = Regex.Match(sourceName ?? "", @"\s—\s(?<quality>[0-9]{3,4})p$", RegexOptions.CultureInvariant);
        var quality = match.Success ? match.Groups["quality"].Value + "p" : "не удалось определить";
        var header = $"Озвучка: {voice}\n\nКачество: {quality}";
        return string.IsNullOrWhiteSpace(overview) ? header : header + "\n\n" + overview;
    }
}
