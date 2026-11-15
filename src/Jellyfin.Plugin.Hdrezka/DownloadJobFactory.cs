using System.Globalization;

namespace Jellyfin.Plugin.Hdrezka;

public static class DownloadJobFactory
{
    public static DownloadJob Create(LibraryVideoRecord record, string title, int? year, string? episodeTitle, string voiceId, int quality, string destinationId)
    {
        if (quality is not (0 or 240 or 360 or 480 or 720 or 1080)) throw new ArgumentOutOfRangeException(nameof(quality));
        var voice = record.Voices.FirstOrDefault(item => item.Id == voiceId) ?? throw new ArgumentException("Озвучка недоступна.", nameof(voiceId));
        _ = ItemKey.EncodePath(record.Path);
        if (string.IsNullOrWhiteSpace(destinationId)) throw new ArgumentException("Не выбрана медиатека.", nameof(destinationId));
        if (record.Season is null && record.Number is null)
            return new DownloadJob(Guid.NewGuid(), record.Path, voice.Id, quality, destinationId, DownloadPaths.Movie(title, year), null, null, DownloadJobStatus.Queued, null, DisplayTitle: title, Year: year);
        if (!int.TryParse(record.Season, NumberStyles.None, CultureInfo.InvariantCulture, out var season) ||
            !int.TryParse(record.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException("Неверный номер серии.", nameof(record));
        return new DownloadJob(Guid.NewGuid(), record.Path, voice.Id, quality, destinationId,
            DownloadPaths.Episode(title, voice.Title, season, number, episodeTitle), season, number, DownloadJobStatus.Queued, null, DisplayTitle: title, EpisodeTitle: episodeTitle, Year: year);
    }
}
