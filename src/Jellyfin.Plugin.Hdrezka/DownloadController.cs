using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record DownloadEnqueueRequest(Guid ItemId, string VoiceId, int Quality, string DestinationId);
public sealed record DownloadBatchRequest(IReadOnlyList<Guid> ItemIds, string VoiceId, int Quality, string DestinationId);
public sealed record DownloadCreateLibraryRequest(string Name, string Path, bool Series);
public sealed record DownloadItemOptions(Guid ItemId, string Title, bool Series, IReadOnlyList<FreeVoice> Voices);

[ApiController]
[Route("Hdrezka/Downloads")]
[Authorize(Policy = "RequiresElevation")]
public sealed class DownloadController(DownloadService downloads, ILibraryManager library, LibraryMediaProvider media, SitemapDetailCoordinator details) : ControllerBase
{
    [HttpGet("Destinations")]
    public ActionResult<IReadOnlyList<DownloadDestination>> Destinations() => Ok(DownloadLibraries.List(library.GetVirtualFolders(), media.Root));

    [HttpGet("CatalogLibraries")]
    public ActionResult CatalogLibraries() => Ok(library.GetVirtualFolders()
        .Where(folder => folder.Name is "Фильмы HDrezka" or "Сериалы HDrezka")
        .Where(folder => folder.Locations.Any(path => Path.GetRelativePath(media.Root, Path.GetFullPath(path)) is "films" or "series"))
        .Select(folder => new { folder.ItemId, folder.Name }));

    [HttpGet("Queue")]
    public async Task<ActionResult<object>> Queue(CancellationToken token)
        => Ok(new { Jobs = await downloads.ListAsync(token).ConfigureAwait(false), downloads.StoppedByMirror, downloads.Progress });

    [HttpPost("Queue/{id:guid}/Cancel")]
    public async Task<ActionResult> Cancel(Guid id, CancellationToken token)
        => await downloads.CancelAsync(id, token).ConfigureAwait(false) ? Ok() : NotFound();

    [HttpDelete("Queue/{id:guid}")]
    public async Task<ActionResult> Remove(Guid id, CancellationToken token)
        => await downloads.RemoveAsync(id, token).ConfigureAwait(false) ? NoContent() : Conflict("Сначала отмените активную задачу и дождитесь её остановки.");

    [HttpDelete("Queue/Finished")]
    public async Task<ActionResult> ClearFinished(CancellationToken token)
        => Ok(new { Removed = await downloads.ClearFinishedAsync(token).ConfigureAwait(false) });

    [HttpGet("Items/{id:guid}")]
    public ActionResult<DownloadItemOptions> Item(Guid id)
    {
        var item = library.GetItemById(id);
        var record = item is null ? null : media.Record(item);
        if (item is not Video || record is null) return NotFound();
        return Ok(new DownloadItemOptions(item.Id, item.Name, record.Season is not null, record.Voices));
    }

    [HttpPost("Items/{id:guid}/Prepare")]
    public async Task<ActionResult> PrepareItem(Guid id, CancellationToken token)
    {
        var item = library.GetItemById(id);
        if (item is Series series)
        {
            if (string.IsNullOrEmpty(series.Path) || LibrarySeriesLocator.Load(media.Root, series.Path) is null) return NotFound();
        }
        else if (item is not Video || media.Record(item) is not { Season: null }) return NotFound();
        var replacement = await details.PrepareAsync(item, token).ConfigureAwait(false);
        var updated = replacement is { } replacementId ? library.GetItemById(replacementId) : library.GetItemById(id);
        return updated is null ? NotFound() : Ok(new { updated.Id, updated.Name });
    }

    [HttpPost("Queue")]
    public async Task<ActionResult> Enqueue([FromBody] DownloadEnqueueRequest request, CancellationToken token)
    {
        var item = library.GetItemById(request.ItemId);
        var record = item is null ? null : media.Record(item);
        if (item is not Video || record is null) return NotFound();
        var series = record.Season is not null;
        var root = DownloadLibraries.Resolve(library.GetVirtualFolders(), media.Root, request.DestinationId, series);
        if (root is null) return BadRequest("Выберите доступную медиатеку подходящего типа.");
        var title = item is MediaBrowser.Controller.Entities.TV.Episode ep ? ep.Series?.Name : item.Name;
        if (string.IsNullOrWhiteSpace(title)) return BadRequest("Не удалось определить название видео.");
        try
        {
            var job = DownloadJobFactory.Create(record, title, item.ProductionYear, series ? item.Name : null, request.VoiceId, request.Quality, request.DestinationId);
            if (System.IO.File.Exists(DownloadPaths.Resolve(root, job.RelativePath))) return Conflict("Файл уже существует.");
            await downloads.AddAsync(job, token).ConfigureAwait(false);
            return Accepted(new { job.Id });
        }
        catch (ArgumentException) { return BadRequest("Проверьте озвучку, качество и выбранную медиатеку."); }
        catch (InvalidOperationException) { return Conflict("Загрузка этого файла уже запланирована."); }
    }

    [HttpPost("Destinations")]
    public async Task<ActionResult> CreateDestination([FromBody] DownloadCreateLibraryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 || !Path.IsPathFullyQualified(request.Path) || !Directory.Exists(request.Path))
            return BadRequest("Укажите название и существующую папку на сервере.");
        if (library.GetVirtualFolders().Any(folder => string.Equals(folder.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict("Медиатека с таким названием уже существует.");
        var kind = request.Series ? CollectionTypeOptions.tvshows : CollectionTypeOptions.movies;
        var candidate = new VirtualFolderInfo { Name = request.Name.Trim(), ItemId = Guid.NewGuid().ToString("N"), CollectionType = kind, Locations = [request.Path] };
        if (DownloadLibraries.List([candidate], media.Root).Count != 1) return BadRequest("Нельзя использовать папку каталога HDrezka.");
        await library.AddVirtualFolder(request.Name.Trim(), kind, new LibraryOptions { PathInfos = [new MediaPathInfo(Path.GetFullPath(request.Path))] }, false).ConfigureAwait(false);
        return Ok();
    }

    [HttpPost("Queue/Batch")]
    public async Task<ActionResult> EnqueueBatch([FromBody] DownloadBatchRequest request, CancellationToken token)
    {
        if (request.ItemIds is null || request.ItemIds.Count is < 1 or > 2000 || request.ItemIds.Distinct().Count() != request.ItemIds.Count)
            return BadRequest("Выберите от 1 до 2000 различных серий.");
        var root = DownloadLibraries.Resolve(library.GetVirtualFolders(), media.Root, request.DestinationId, true);
        if (root is null) return BadRequest("Выберите медиатеку сериалов.");
        var jobs = new List<DownloadJob>();
        var skipped = 0;
        var existingJobs = await downloads.ListAsync(token).ConfigureAwait(false);
        string? contentId = null;
        foreach (var id in request.ItemIds)
        {
            if (library.GetItemById(id) is not MediaBrowser.Controller.Entities.TV.Episode item || media.Record(item) is not { } record || record.Season is null)
                return BadRequest("Не удалось найти одну из серий HDrezka.");
            contentId ??= record.Id;
            if (record.Id != contentId) return BadRequest("Выберите серии одного сериала.");
            if (string.IsNullOrWhiteSpace(item.Series?.Name)) return BadRequest("Не удалось определить название сериала.");
            if (!record.Voices.Any(voice => voice.Id == request.VoiceId)) { skipped++; continue; }
            try
            {
                var job = DownloadJobFactory.Create(record, item.Series.Name, item.ProductionYear, item.Name, request.VoiceId, request.Quality, request.DestinationId);
                if (System.IO.File.Exists(DownloadPaths.Resolve(root, job.RelativePath))) { skipped++; continue; }
                if (existingJobs.Any(existing => existing.Status is DownloadJobStatus.Queued or DownloadJobStatus.Downloading && existing.LibraryId == job.LibraryId &&
                    string.Equals(existing.RelativePath, job.RelativePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) { skipped++; continue; }
                jobs.Add(job);
            }
            catch (ArgumentException) { return BadRequest("Проверьте озвучку и качество."); }
        }
        if (jobs.Count == 0) return Ok(new { Added = 0, Skipped = skipped });
        try { await downloads.AddRangeAsync(jobs, token).ConfigureAwait(false); }
        catch (InvalidOperationException) { return Conflict("Одна из выбранных серий уже находится в очереди."); }
        return Accepted(new { Added = jobs.Count, Skipped = skipped });
    }
}
