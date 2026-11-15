namespace Jellyfin.Plugin.Hdrezka;

public sealed class DownloadQueue
{
    private readonly DownloadQueueStore _store;
    private readonly List<DownloadJob> _jobs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _worker = new(1, 1);
    private Guid? _currentId;

    private DownloadQueue(DownloadQueueStore store, IEnumerable<DownloadJob> jobs) { _store = store; _jobs = jobs.ToList(); }

    public static async Task<DownloadQueue> OpenAsync(DownloadQueueStore store, CancellationToken token)
        => new(store, await store.LoadAsync(token).ConfigureAwait(false));

    public async Task<IReadOnlyList<DownloadJob>> ListAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return _jobs.ToArray(); }
        finally { _gate.Release(); }
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var index = _jobs.FindIndex(job => job.Id == id && job.Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading);
            if (index < 0) return false;
            var original = _jobs[index];
            _jobs[index] = original with { Status = DownloadJobStatus.Cancelled, Error = null };
            try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
            catch { _jobs[index] = original; throw; }
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var index = _jobs.FindIndex(job => job.Id == id && IsFinished(job) && job.Id != _currentId);
            if (index < 0) return false;
            var item = _jobs[index];
            _jobs.RemoveAt(index);
            try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
            catch { _jobs.Insert(index, item); throw; }
            return true;
        }
        finally { _gate.Release(); }
    }

    public async Task<int> ClearFinishedAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var before = _jobs.ToArray();
            var count = _jobs.RemoveAll(job => IsFinished(job) && job.Id != _currentId);
            if (count == 0) return 0;
            try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
            catch { _jobs.Clear(); _jobs.AddRange(before); throw; }
            return count;
        }
        finally { _gate.Release(); }
    }

    private static bool IsFinished(DownloadJob job) => job.Status is DownloadJobStatus.Completed or DownloadJobStatus.Failed or DownloadJobStatus.Cancelled;

    public async Task SetActualQualityAsync(Guid id, int quality, CancellationToken token, int? width = null, int? height = null)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var index = _jobs.FindIndex(job => job.Id == id && job.Status == DownloadJobStatus.Downloading);
            if (index < 0) return;
            var original = _jobs[index];
            _jobs[index] = original with { ActualQuality = quality, Width = width, Height = height };
            try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
            catch { _jobs[index] = original; throw; }
        }
        finally { _gate.Release(); }
    }

    public Task AddAsync(DownloadJob job, CancellationToken token) => AddRangeAsync([job], token);

    public async Task AddRangeAsync(IReadOnlyList<DownloadJob> jobs, CancellationToken token)
    {
        if (jobs.Any(job => job.Status != DownloadJobStatus.Queued || job.Id == Guid.Empty)) throw new ArgumentException("Неверная задача загрузки.", nameof(jobs));
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var active = _jobs.Where(existing => existing.Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading).ToList();
            foreach (var job in jobs)
            {
                if (_jobs.Any(existing => existing.Id == job.Id) || active.Any(existing => existing.Id == job.Id)) throw new InvalidOperationException("Задача уже существует.");
                if (active.Any(existing => existing.LibraryId == job.LibraryId && string.Equals(existing.RelativePath, job.RelativePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                    throw new InvalidOperationException("Загрузка этого файла уже запланирована.");
                active.Add(job);
            }
            var oldCount = _jobs.Count;
            _jobs.AddRange(jobs);
            try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
            catch { _jobs.RemoveRange(oldCount, _jobs.Count - oldCount); throw; }
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> ProcessNextAsync(Func<DownloadJob, CancellationToken, Task> execute, CancellationToken token)
    {
        if (!await _worker.WaitAsync(0, token).ConfigureAwait(false)) return false;
        try
        {
            DownloadJob? job;
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var index = _jobs.FindIndex(item => item.Status == DownloadJobStatus.Queued);
                if (index < 0) return false;
                job = _jobs[index] with { Status = DownloadJobStatus.Downloading, Error = null };
                var original = _jobs[index];
                _jobs[index] = job;
                try { await _store.SaveAsync(_jobs, token).ConfigureAwait(false); }
                catch { _jobs[index] = original; throw; }
                _currentId = job.Id;
            }
            finally { _gate.Release(); }
            DownloadJob updated;
            try { await execute(job, token).ConfigureAwait(false); updated = job with { Status = DownloadJobStatus.Completed }; }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { updated = job with { Status = DownloadJobStatus.Queued }; }
            catch (MirrorAccessException ex) when (ex.StatusCode == 500) { updated = job with { Status = DownloadJobStatus.Failed, Error = "Зеркало вернуло HTTP 500; запросы остановлены." }; }
            catch (Exception) { updated = job with { Status = DownloadJobStatus.Failed, Error = "Не удалось загрузить видео." }; }
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                var index = _jobs.FindIndex(item => item.Id == job.Id);
                if (index >= 0 && _jobs[index].Status != DownloadJobStatus.Cancelled)
                    _jobs[index] = _jobs[index] with { Status = updated.Status, Error = updated.Error };
                await _store.SaveAsync(_jobs, CancellationToken.None).ConfigureAwait(false);
            }
            finally { _gate.Release(); }
            return true;
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try { _currentId = null; }
            finally { _gate.Release(); _worker.Release(); }
        }
    }
}
