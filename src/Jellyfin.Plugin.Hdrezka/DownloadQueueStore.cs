using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public enum DownloadJobStatus { Queued, Downloading, Completed, Failed, Cancelled }

public sealed record DownloadJob(Guid Id, string ContentPath, string VoiceId, int Quality, string LibraryId,
    string RelativePath, int? Season, int? Episode, DownloadJobStatus Status, string? Error, int? ActualQuality = null,
    string? DisplayTitle = null, string? EpisodeTitle = null, int? Year = null, int? Width = null, int? Height = null);

public sealed class DownloadQueueStore(string directory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string QueuePath => Path.Combine(directory, "queue.json");

    public async Task<IReadOnlyList<DownloadJob>> LoadAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!File.Exists(QueuePath)) return [];
            await using var stream = File.OpenRead(QueuePath);
            var jobs = await JsonSerializer.DeserializeAsync<List<DownloadJob>>(stream, cancellationToken: token).ConfigureAwait(false) ?? [];
            return jobs.Select(job => job.Status == DownloadJobStatus.Downloading ? job with { Status = DownloadJobStatus.Queued, Error = null } : job).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IReadOnlyList<DownloadJob> jobs, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        var temporary = Path.Combine(directory, "queue-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(stream, jobs, cancellationToken: token).ConfigureAwait(false);
            File.Move(temporary, QueuePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); _gate.Release(); }
    }
}
