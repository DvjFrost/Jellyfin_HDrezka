using System.Globalization;

namespace Jellyfin.Plugin.Hdrezka;

public static class EpisodeTitleSelection
{
    public static string Choose(string? existing, string? candidate, int number)
    {
        if (!string.IsNullOrWhiteSpace(existing) && !IsGeneric(existing, number)) return existing;
        return string.IsNullOrWhiteSpace(candidate) ? existing ?? $"Серия {number}" : candidate;
    }

    public static bool ShouldRepair(string? current, string? path, string? candidate, int number, bool locked)
        => !locked && !string.IsNullOrWhiteSpace(candidate) && !IsGeneric(candidate, number) &&
           !string.Equals(current, candidate, StringComparison.Ordinal) &&
           (string.IsNullOrWhiteSpace(current) || IsGeneric(current, number) ||
            (!string.IsNullOrEmpty(path) && string.Equals(current, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal)));

    private static bool IsGeneric(string value, int number)
    {
        var suffix = number.ToString(CultureInfo.InvariantCulture);
        return string.Equals(value.Trim(), "Серия " + suffix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value.Trim(), "Episode " + suffix, StringComparison.OrdinalIgnoreCase);
    }
}
