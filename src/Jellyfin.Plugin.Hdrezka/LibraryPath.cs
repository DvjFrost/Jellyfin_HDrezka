namespace Jellyfin.Plugin.Hdrezka;

public static class LibraryPath
{
    public static string TitleDirectory(string id) => "HDrezka-" + Validate(id, nameof(id));
    public static string PrimaryFileName(string id) => TitleDirectory(id) + ".strm";
    public static string VersionFileName(string id, string voiceId) => $"{TitleDirectory(id)} - voice-{Validate(voiceId, nameof(voiceId))}.strm";

    private static string Validate(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 16 || !value.All(char.IsAsciiDigit)) throw new ArgumentException("Expected numeric identifier.", name);
        return value;
    }
}
