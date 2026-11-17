using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using MediaBrowser.Model.Session;
using MediaBrowser.Controller.Session;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class SitemapDetailFilter(SitemapDetailCoordinator coordinator, ILibraryManager library, IUserManager users, LibraryMediaProvider media, ISessionManager? sessions = null) : IAsyncActionFilter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> EpisodeRepairGates = new();
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        BaseItem? detailsItem = null;
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (sessions is not null && context.HttpContext.User.Identity?.IsAuthenticated == true && action?.ControllerName == "Playstate" && action.ActionName == "ReportPlaybackStopped"
            && context.ActionArguments.TryGetValue("playbackStopInfo", out var multipartStop) && multipartStop is PlaybackStopInfo stopped
            && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var multipartUser)
            && context.HttpContext.User.FindFirst("Jellyfin-DeviceId")?.Value is { Length: > 0 } multipartDevice
            && sessions.Sessions.FirstOrDefault(s => s.UserId == multipartUser && s.DeviceId == multipartDevice && s.NowPlayingItem?.Id == stopped.ItemId) is { } session
            && library.GetItemById(stopped.ItemId) is Video multipartItem && media.Record(multipartItem) is { MultipartMovie: true } multipartRecord
            && MultipartContinuation.Finished(stopped.PositionTicks, multipartItem.RunTimeTicks, stopped.Failed)
            && !string.IsNullOrEmpty(multipartItem.Path))
        {
            var dir = Path.GetDirectoryName(multipartItem.Path)!;
            var isPart = Path.GetFileName(dir) == ".hdrezka-parts";
            if (isPart) dir = Path.GetDirectoryName(dir)!;
            var match = System.Text.RegularExpressions.Regex.Match(Path.GetFileNameWithoutExtension(multipartItem.Path), @" - pt([0-9]{1,4})$");
            var index = match.Success && int.TryParse(match.Groups[1].Value, out var parsed) ? parsed : 1;
            var nextPath = Path.Combine(dir, ".hdrezka-parts", LibraryPath.TitleDirectory(multipartRecord.Id) + " - pt" + (index + 1).ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".strm");
            var nextId = library.GetNewItemId(nextPath, typeof(Video));
            if (System.IO.File.Exists(nextPath) && library.GetItemById<Video>(nextId) is not null && !(stopped.NowPlayingQueue?.Any(q => q.Id == nextId) ?? false))
            {
                NextEpisodeVoice.Shared.Stopped(context.HttpContext.User.FindFirst("Jellyfin-UserId")!.Value, multipartDevice, multipartRecord, stopped.PositionTicks);
                await next().ConfigureAwait(false);
                await sessions.SendPlayCommand(session.Id, session.Id, new PlayRequest { ItemIds = [nextId], PlayCommand = PlayCommand.PlayNow, ControllingUserId = multipartUser }, context.HttpContext.RequestAborted).ConfigureAwait(false);
                return;
            }
        }
        if (context.HttpContext.User.Identity?.IsAuthenticated == true && action?.ControllerName == "Playstate" && action.ActionName == "ReportPlaybackStopped"
            && context.ActionArguments.TryGetValue("playbackStopInfo", out var stopArgument) && stopArgument is PlaybackStopInfo stop && !stop.Failed
            && context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value is { Length: > 0 } stopUser
            && context.HttpContext.User.FindFirst("Jellyfin-DeviceId")?.Value is { Length: > 0 } stopDevice
            && library.GetItemById(stop.ItemId) is { } stoppedItem && media.Record(stoppedItem) is { } stoppedRecord)
            NextEpisodeVoice.Shared.Stopped(stopUser, stopDevice, stoppedRecord, stop.PositionTicks);
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && SitemapDetailRoute.IsEpisodeList(context.HttpContext.Request.Method, action?.ControllerName, action?.ActionName)
            && context.ActionArguments.TryGetValue("seriesId", out var seriesArgument) && seriesArgument is Guid seriesId && seriesId != Guid.Empty
            && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var episodeUserId)
            && episodeUserId != Guid.Empty && users.GetUserById(episodeUserId) is { } episodeUser
            && library.GetItemById<Series>(seriesId, episodeUser) is { Path: not null } episodeSeries
            && LibrarySeriesLocator.Load(media.Root, episodeSeries.Path) is not null)
        {
            var gate = EpisodeRepairGates.GetOrAdd(seriesId, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
            try { await LibraryVersionTitles.RepairAsync(episodeSeries, library, media, context.HttpContext.RequestAborted).ConfigureAwait(false); }
            finally { gate.Release(); }
            // Jellyfin Web requests playback queues starting at the selected episode. Include
            // earlier entries so the player can navigate backwards after a direct launch.
            if (context.ActionArguments.TryGetValue("startItemId", out var queueStartArg) && queueStartArg is Guid queueStart && queueStart != Guid.Empty
                && context.ActionArguments.TryGetValue("limit", out var queueLimitArg) && queueLimitArg is int queueLimit && queueLimit == 100
                && context.ActionArguments.TryGetValue("fields", out var queueFieldsArg) && queueFieldsArg is ItemFields[] queueFields
                && queueFields.Contains(ItemFields.Chapters) && queueFields.Contains(ItemFields.MediaSources) && queueFields.Contains(ItemFields.Trickplay)
                && (!context.ActionArguments.TryGetValue("seasonId", out var seasonArg) || seasonArg is null)
                && (!context.ActionArguments.TryGetValue("season", out var seasonNumberArg) || seasonNumberArg is null)
                && (!context.ActionArguments.TryGetValue("startIndex", out var indexArg) || indexArg is null))
            {
                var primary = library.GetItemById<Video>(queueStart)?.PrimaryVersionId ?? queueStart;
                var episodeIds = episodeSeries.GetEpisodes(episodeUser, new DtoOptions(), false).Select(x => x.Id).ToArray();
                context.ActionArguments["startItemId"] = PlaybackQueueWindow.FindStart(episodeIds, primary);
                context.ActionArguments["limit"] = queueLimit + PlaybackQueueWindow.EarlierEpisodes;
            }
        }
        Guid? requestedQuality = null;
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && context.ActionArguments.TryGetValue("startItemId", out var startArgument)
            && startArgument is Guid startId && startId != Guid.Empty
            && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var startUserId)
            && startUserId != Guid.Empty && users.GetUserById(startUserId) is { } startUser
            && media.FindQualityOption(startId) is { } startOption
            && Guid.TryParse(startOption.VersionId, out var startActualId)
            && library.GetItemById<BaseItem>(startActualId, startUser) is not null)
            context.ActionArguments["startItemId"] = startActualId;
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && context.ActionArguments.TryGetValue("itemId", out var requestedArgument)
            && requestedArgument is Guid requestedId && requestedId != Guid.Empty
            && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var requestingUserId)
            && requestingUserId != Guid.Empty
            && users.GetUserById(requestingUserId) is { } requestingUser
            && media.FindQualityOption(requestedId) is { } requestedOption
            && Guid.TryParse(requestedOption.VersionId, out var actualId)
            && library.GetItemById<BaseItem>(actualId, requestingUser) is not null)
        {
            requestedQuality = requestedId;
            context.ActionArguments["itemId"] = actualId;
        }
        var isPlaybackInfo = action is not null && string.Equals(action.ControllerName, "MediaInfo", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(action.ActionName, "GetPostedPlaybackInfo", StringComparison.OrdinalIgnoreCase) || string.Equals(action.ActionName, "GetPlaybackInfo", StringComparison.OrdinalIgnoreCase));
        var isStreaming = action is not null && (string.Equals(action.ControllerName, "DynamicHls", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action.ControllerName, "Videos", StringComparison.OrdinalIgnoreCase));
        if ((isPlaybackInfo || isStreaming)
            && context.ActionArguments.TryGetValue("itemId", out var playbackItem) && playbackItem is Guid playbackId && playbackId != Guid.Empty)
        {
            context.ActionArguments.TryGetValue("mediaSourceId", out var selected);
            context.ActionArguments.TryGetValue("playbackInfoDto", out var body);
            var previous = PlaybackSelection.Current;
            PlaybackSelection.Current = PlaybackSelection.Resolve(selected as string, body, requestedQuality ?? playbackId);
            try { await next().ConfigureAwait(false); }
            finally { PlaybackSelection.Current = previous; }
            return;
        }
        if (context.HttpContext.User.Identity?.IsAuthenticated == true
            && SitemapDetailRoute.IsDetail(context.HttpContext.Request.Method, action?.ControllerName, action?.ActionName)
            && context.ActionArguments.TryGetValue("itemId", out var argument) && argument is Guid id && id != Guid.Empty
            && Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var userId)
            && userId != Guid.Empty
            && users.GetUserById(userId) is { } user)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                var item = library.GetItemById<BaseItem>(id, user);
                if (item is not null && context.HttpContext.Request.Path.StartsWithSegments("/Users") && context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value is { Length: > 0 } viewUser && context.HttpContext.User.FindFirst("Jellyfin-DeviceId")?.Value is { Length: > 0 } viewDevice && media.Record(item) is { } viewedRecord)
                    NextEpisodeVoice.Shared.ClearForViewedEpisode(viewUser, viewDevice, viewedRecord);
                if (item is null && SeriesMigration.FindReplacement(media.Root, id) is { } previous && library.GetItemById<BaseItem>(previous, user) is not null)
                {
                    context.ActionArguments["itemId"] = previous;
                    detailsItem = library.GetItemById<BaseItem>(previous, user);
                }
                else if (item is not null && await coordinator.PrepareAsync(item, timeout.Token).ConfigureAwait(false) is { } replacement)
                {
                    context.ActionArguments["itemId"] = replacement;
                    detailsItem = library.GetItemById<BaseItem>(replacement, user);
                }
                else detailsItem = item;
            }
            catch (OperationCanceledException) when (!context.HttpContext.RequestAborted.IsCancellationRequested) { }
        }
        var executed = await next().ConfigureAwait(false);
        if (requestedQuality is { } qualityId && executed.Result is ObjectResult { Value: BaseItemDto resolvedDto })
            resolvedDto.Id = qualityId;
        if (detailsItem is Video video && media.Record(video) is { Voices.Count: 1 } record && (record.Season is null || record.MultipartMovie) &&
            executed.Result is ObjectResult { Value: BaseItemDto dto })
        {
            string? sourceName = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                sourceName = (await media.GetMediaSourcesForAsync(video, video.Id.ToString("N"), timeout.Token).ConfigureAwait(false)).FirstOrDefault()?.Name;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !context.HttpContext.RequestAborted.IsCancellationRequested) { }
            dto.Overview = SingleVoiceDetails.Format(record.Voices[0].Title, sourceName, dto.Overview);
        }
    }
}
