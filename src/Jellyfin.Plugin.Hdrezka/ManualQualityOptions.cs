namespace Jellyfin.Plugin.Hdrezka;

public sealed record ManualQualityOption(string Id, string VersionId, string Name, int Quality);

public static class ManualQualityOptions
{
    public static IReadOnlyList<ManualQualityOption> Create(string versionId, string voiceName, IEnumerable<NamedStream> streams) => streams
        .Where(x => x.Stream.Quality is > 0 and <= 1080)
        .Select(x => x.Stream.Quality)
        .Distinct()
        .OrderByDescending(x => x)
        .Select(quality => new ManualQualityOption(MediaSelection.SourceId(versionId, quality.ToString(System.Globalization.CultureInfo.InvariantCulture)), versionId, $"{voiceName} — {quality}p", quality))
        .ToArray();
}
