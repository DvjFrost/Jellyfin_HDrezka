namespace Jellyfin.Plugin.Hdrezka;

public static class EpisodeVersionPaths
{
    public static IReadOnlyList<string> Collect(string primaryPath, IReadOnlyList<FreeVoice> voices)
    {
        var dir = Path.GetDirectoryName(primaryPath) ?? throw new ArgumentException("Missing directory", nameof(primaryPath));
        var stem = Path.GetFileNameWithoutExtension(primaryPath);
        return voices.Skip(1).Select(v => Path.Combine(dir, stem + " - voice-" + v.Id + ".strm")).Where(File.Exists).ToArray();
    }
}
