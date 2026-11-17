using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Hdrezka;

public static class DownloadPaths
{
    public static string Resolve(string libraryRoot, string relativePath)
    {
        var root = Path.GetFullPath(libraryRoot);
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Путь должен находиться внутри медиатеки.");
        var destination = Path.GetFullPath(Path.Combine(root, relativePath));
        var relative = Path.GetRelativePath(root, destination);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ArgumentException("Путь должен находиться внутри медиатеки.");
        var directory = Path.GetDirectoryName(destination);
        while (directory is not null && !string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Путь загрузки содержит ссылку на другую папку.");
            directory = Path.GetDirectoryName(directory);
        }
        return destination;
    }

    public static string Movie(string title, int? year)
        => Component(title) + (year is >= 1888 and <= 9999 ? $" ({year.Value.ToString(CultureInfo.InvariantCulture)})" : "") + ".mp4";

    public static string MoviePart(string title, int? year, int part)
    {
        if (part is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(part));
        return Path.GetFileNameWithoutExtension(Movie(title, year)) + " - pt" + part.ToString(CultureInfo.InvariantCulture) + ".mp4";
    }

    public static string Episode(string title, string voice, int season, int episode, string? episodeTitle)
    {
        if (season is < 0 or > 999) throw new ArgumentOutOfRangeException(nameof(season));
        if (episode is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(episode));
        var directory = Component(title) + " (" + Component(voice) + ")";
        var episodeName = string.IsNullOrWhiteSpace(episodeTitle) ? $"Серия {episode}" : episodeTitle;
        return Path.Combine(directory, $"Сезон {season:D2}", $"S{season:D2}E{episode:D2} {Component(episodeName)}.mp4");
    }

    private static string Component(string? text)
    {
        var result = new StringBuilder();
        foreach (var character in (text ?? "").Normalize(NormalizationForm.FormC))
        {
            if (result.Length >= 100) break;
            result.Append(char.IsControl(character) || "<>:\"/\\|?*".Contains(character) ? ' ' : character);
        }
        var value = result.ToString().Trim(' ', '.');
        if (value.Length == 0) value = "Без названия";
        var stem = value.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) value = "_" + value;
        return value;
    }
}
