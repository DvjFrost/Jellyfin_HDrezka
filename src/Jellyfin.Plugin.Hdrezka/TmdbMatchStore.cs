using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public enum TmdbMatchStatus { Matched, Uncertain, NoResults }
public sealed record TmdbMatch(string Id, TmdbMatchStatus Status, int? TmdbId, string? Title, string? PosterPath, string? MediaType = null, string? QueryFingerprint = null);

public sealed class TmdbMatchStore(string path)
{
    public async Task<IReadOnlyDictionary<string,TmdbMatch>> LoadAsync(CancellationToken token)
    {
        var records = new Dictionary<string,TmdbMatch>(StringComparer.Ordinal);
        if (!File.Exists(path)) return records;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(token).ConfigureAwait(false);
        var lastNewline = content.LastIndexOf('\n');
        if (lastNewline < 0) return records;
        foreach (var line in content[..(lastNewline + 1)].Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<TmdbMatch>(line);
            if (record is null || string.IsNullOrWhiteSpace(record.Id) || !record.Id.All(char.IsAsciiDigit) || !Enum.IsDefined(record.Status)) throw new JsonException("Invalid TMDB match record.");
            records[record.Id] = record;
        }
        return records;
    }

    public async Task AppendAsync(TmdbMatch record, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(record.Id) || !record.Id.All(char.IsAsciiDigit) || !Enum.IsDefined(record.Status)) throw new ArgumentException("Invalid TMDB match record.", nameof(record));
        var absolute = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        await using var stream = new FileStream(absolute, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, true);
        if (stream.Length > 0)
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n')
            {
                var pos = stream.Length - 1;
                while (pos >= 0) { stream.Position = pos; if (stream.ReadByte() == '\n') break; pos--; }
                stream.SetLength(pos + 1);
            }
        }
        stream.Seek(0, SeekOrigin.End);
        await using var writer = new StreamWriter(stream);
        await writer.WriteLineAsync(JsonSerializer.Serialize(record).AsMemory(), token).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
    }
}
