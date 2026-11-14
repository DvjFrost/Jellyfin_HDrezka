using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class NextEpisodeVoice
{
    public static NextEpisodeVoice Shared { get; } = new();
    private sealed record Playback(LibraryVideoRecord Record, string Voice, long Duration);
    private sealed record Pending(LibraryVideoRecord Record, string Voice, DateTimeOffset Expiry);
    private readonly ConcurrentDictionary<string, Playback> _playing = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private static string Key(string user, string device) => user + "\0" + device;
    private static bool Same(LibraryVideoRecord a, LibraryVideoRecord b) => a.Id == b.Id && a.Season == b.Season && a.Number == b.Number;
    public void Observe(string user, string device, LibraryVideoRecord record, string voice, long? duration)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(device) || record.Season is null || record.Number is null || duration is null or <= 0) return;
        _playing[Key(user, device)] = new(record, voice, duration.Value);
    }
    public void Stopped(string user, string device, LibraryVideoRecord record, long? position)
    {
        var key = Key(user, device);
        if (!_playing.TryRemove(key, out var playback) || !Same(playback.Record, record) || position is null || position.Value < playback.Duration * 0.95)
        { _pending.TryRemove(key, out _); return; }
        _pending[key] = new(record, playback.Voice, DateTimeOffset.UtcNow.AddMinutes(2));
    }
    public string? ForNext(string user, string device, LibraryVideoRecord next)
    {
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(device) || !_pending.TryGetValue(Key(user, device), out var previous) || previous.Expiry <= DateTimeOffset.UtcNow || previous.Record.Id != next.Id) return null;
        if (!int.TryParse(previous.Record.Season, out var oldSeason) || !int.TryParse(previous.Record.Number, out var oldNumber) || !int.TryParse(next.Season, out var season) || !int.TryParse(next.Number, out var number)) return null;
        return (season == oldSeason && number == oldNumber + 1) || (season == oldSeason + 1 && number == 1) ? previous.Voice : null;
    }
    public void ClearForViewedEpisode(string user, string device, LibraryVideoRecord record)
    {
        if (ForNext(user, device, record) is not null) _pending.TryRemove(Key(user, device), out _);
    }
}
