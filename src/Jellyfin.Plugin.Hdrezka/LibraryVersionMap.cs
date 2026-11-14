using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record LibraryVersion(string Id, string Path);

public static class LibraryVersionMap
{
    public static IReadOnlyDictionary<string, string> Create(LibraryVideoRecord record, IEnumerable<LibraryVersion> versions)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = record.Voices.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var version in versions)
        {
            var file = Path.GetFileNameWithoutExtension(version.Path);
            var match = Regex.Match(file, @" - voice-([0-9]{1,16})$", RegexOptions.CultureInvariant);
            var voice = match.Success ? match.Groups[1].Value : record.Voices.FirstOrDefault()?.Id;
            if (voice is not null && allowed.Contains(voice)) map.TryAdd(voice, version.Id);
        }
        return map;
    }
}
