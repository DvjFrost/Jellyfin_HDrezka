using System.Reflection;
using System.Runtime.ExceptionServices;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Hdrezka;

public class LibrarySourceProxy : DispatchProxy
{
    public IMediaSourceManager Inner { get; set; } = null!;
    public LibraryMediaProvider Provider { get; set; } = null!;
    public ILibraryManager Library { get; set; } = null!;
    public IHttpContextAccessor? Http { get; set; }

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        var item = args?.FirstOrDefault() as BaseItem;
        var record = item is null ? null : Provider.Record(item);
        var owned = record is not null;
        if (owned && method.Name == nameof(IMediaSourceManager.GetStaticMediaSources))
        {
            var local = Inner.GetStaticMediaSources(item!, (bool)args![1]!, args.Length > 2 ? args[2] as Jellyfin.Database.Implementations.Entities.User : null);
            var versions = local.Select(x => Guid.TryParse(x.Id, out var id) ? Library.GetItemById(id) : null)
                .Where(x => x?.Path is not null).Select(x => new LibraryVersion(x!.Id.ToString("N"), x.Path!));
            var mapping = LibraryVersionMap.Create(record!, versions);
            var names = record!.Voices.Where(v => mapping.ContainsKey(v.Id)).ToDictionary(v => mapping[v.Id], v => v.Title, StringComparer.OrdinalIgnoreCase);
            var sources = local.Where(x => names.ContainsKey(x.Id)).ToArray();
            foreach (var source in sources) source.Name = names[source.Id];
            if (sources.Length > 0) return (IReadOnlyList<MediaSourceInfo>)sources;
            return (IReadOnlyList<MediaSourceInfo>)[new MediaSourceInfo
            {
                Id = item!.Id.ToString("N"), Name = "Нет бесплатных озвучек", Type = MediaSourceType.Placeholder,
                SupportsDirectPlay = false, SupportsDirectStream = false, SupportsTranscoding = false
            }];
        }
        if (owned && method.Name == nameof(IMediaSourceManager.GetMediaSource) && args![1] is string sourceId)
            return FindSource(item!, sourceId, (CancellationToken)args[4]!);
        if (owned && method.Name == nameof(IMediaSourceManager.GetPlaybackMediaSources)) args![2] = false;
        object? result;
        try { result = method.Invoke(Inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
        if (owned && method.Name == nameof(IMediaSourceManager.GetPlaybackMediaSources))
            return Filter((Task<IReadOnlyList<MediaSourceInfo>>)result!);
        return result;
    }

    private async Task<MediaSourceInfo> FindSource(BaseItem item, string sourceId, CancellationToken token)
    {
        var normalized = Guid.TryParse(sourceId, out var id) ? id.ToString("N") : sourceId;
        return (await Provider.GetMediaSourcesForAsync(item, normalized, token).ConfigureAwait(false)).First(x => string.Equals(x.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<IReadOnlyList<MediaSourceInfo>> Filter(Task<IReadOnlyList<MediaSourceInfo>> task)
        => (await task.ConfigureAwait(false)).Where(source => !string.Equals(source.Path, MovieLibraryWriter.Placeholder, StringComparison.OrdinalIgnoreCase)).ToArray();
}
