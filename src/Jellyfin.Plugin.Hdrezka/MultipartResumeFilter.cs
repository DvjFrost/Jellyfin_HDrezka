using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Jellyfin.Data.Enums;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class MultipartResumeFilter(ILibraryManager library, IUserManager users, IUserDataManager data, IDtoService dto, LibraryMediaProvider media, ISessionManager sessions) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (!Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var userId) || users.GetUserById(userId) is not { } user)
        { await next().ConfigureAwait(false); return; }

        Guid itemId = Guid.Empty;
        if (action?.ControllerName == "Playstate" && action.ActionName == "ReportPlaybackStopped" && context.ActionArguments.TryGetValue("playbackStopInfo", out var stopArg) && stopArg is PlaybackStopInfo stop && !stop.Failed) itemId = stop.ItemId;
        if (action?.ControllerName == "Playstate" && action.ActionName == "ReportPlaybackProgress" && context.ActionArguments.TryGetValue("playbackProgressInfo", out var progressArg) && progressArg is PlaybackProgressInfo progress) itemId = progress.ItemId;
        var part = itemId != Guid.Empty ? library.GetItemById<Video>(itemId, user) : null;
        var record = part is null ? null : media.Record(part);
        var film = part?.Path is { } partPath && record?.MultipartMovie == true ? FindFilm(partPath, record.Id) : null;

        if (action?.ControllerName == "Image" && action.ActionName is "GetItemImage" or "GetItemImageByIndex"
            && context.ActionArguments.TryGetValue("itemId", out var imageArg) && imageArg is Guid imageId
            && library.GetItemById<Video>(imageId, user) is { Path: not null } imagePart
            && media.Record(imagePart) is { MultipartMovie: true } imageRecord
            && FindFilm(imagePart.Path, imageRecord.Id) is { } imageFilm)
            context.ActionArguments["itemId"] = imageFilm.Id;

        var executed = await next().ConfigureAwait(false);
        if (SitemapDetailRoute.IsDetail(context.HttpContext.Request.Method, action?.ControllerName, action?.ActionName)
            && context.ActionArguments.TryGetValue("itemId", out var detailArg) && detailArg is Guid detailId
            && library.GetItemById<Video>(detailId, user) is { Path: not null } detailPart
            && media.Record(detailPart) is { MultipartMovie: true } detailRecord
            && Path.GetFileName(Path.GetDirectoryName(detailPart.Path)) == ".hdrezka-parts"
            && FindFilm(detailPart.Path, detailRecord.Id) is { } detailFilm
            && executed.Result is ObjectResult { Value: BaseItemDto detailDto })
        {
            var main = dto.GetBaseItemDto(detailFilm, new DtoOptions(), user);
            detailDto.Name = main.Name;
            detailDto.Type = BaseItemKind.Movie;
            detailDto.Overview = main.Overview;
            detailDto.ImageTags = main.ImageTags;
            detailDto.BackdropImageTags = main.BackdropImageTags;
        }
        try
        {
            if (film is not null && part is not null && part.Id != film.Id)
            {
                var state = data.GetUserData(user, part);
                if (state?.PlaybackPositionTicks > 0) await MultipartResumeStore.PutAsync(media.Root, userId, film.Id, part.Id, context.HttpContext.RequestAborted).ConfigureAwait(false);
                else MultipartResumeStore.Remove(media.Root, userId, film.Id);
            }
            else if (film is not null && part?.Id == film.Id) MultipartResumeStore.Remove(media.Root, userId, film.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        if (action?.ControllerName == "Items" && action.ActionName is "GetResumeItems" or "GetResumeItemsLegacy"
            && executed.Result is ObjectResult { Value: QueryResult<BaseItemDto> page } && page.StartIndex == 0
            && (!context.ActionArguments.TryGetValue("userId", out var requested) || requested is null || requested is Guid id && (id == Guid.Empty || id == userId))
            && (!context.ActionArguments.TryGetValue("parentId", out var parent) || parent is null || parent is Guid parentGuid && parentGuid == Guid.Empty)
            && (!context.ActionArguments.TryGetValue("searchTerm", out var search) || search is null || search is string text && text.Length == 0)
            && (!context.ActionArguments.TryGetValue("includeItemTypes", out var includes) || includes is not BaseItemKind[] includeKinds || includeKinds.Length == 0 || includeKinds.Contains(BaseItemKind.Movie))
            && (!context.ActionArguments.TryGetValue("excludeItemTypes", out var excludes) || excludes is not BaseItemKind[] excludeKinds || !excludeKinds.Contains(BaseItemKind.Movie))
            && (!context.ActionArguments.TryGetValue("mediaTypes", out var mediaTypes) || mediaTypes is not MediaType[] types || types.Length == 0 || types.Contains(MediaType.Video)))
        {
            var extra = new List<BaseItemDto>();
            var replacedFilms = new HashSet<Guid>();
            foreach (var saved in MultipartResumeStore.List(media.Root, userId))
            {
                if (library.GetItemById<Movie>(saved.Film, user) is not { } movie || library.GetItemById<Video>(saved.Part, user) is not { Path: not null } savedPart
                    || media.Record(savedPart) is not { MultipartMovie: true } savedRecord || FindFilm(savedPart.Path, savedRecord.Id)?.Id != movie.Id) continue;
                var partData = data.GetUserData(user, savedPart);
                if (partData?.PlaybackPositionTicks is not > 0) continue;
                if (context.ActionArguments.TryGetValue("excludeActiveSessions", out var excludeActive) && excludeActive is true &&
                    sessions.Sessions.Any(s => s.UserId == userId && (s.NowPlayingItem?.Id == savedPart.Id || s.NowPlayingItem?.Id == movie.Id))) continue;
                var movieDto = dto.GetBaseItemDto(movie, new DtoOptions(), user);
                var partDto = dto.GetBaseItemDto(savedPart, new DtoOptions(), user);
                movieDto.Id = savedPart.Id;
                movieDto.UserData = partDto.UserData;
                movieDto.RunTimeTicks = savedPart.RunTimeTicks;
                extra.Add(movieDto);
                replacedFilms.Add(movie.Id);
            }
            if (extra.Count > 0)
            {
                var replaced = page.Items.Count(x => replacedFilms.Contains(x.Id) || extra.Any(e => e.Id == x.Id));
                var all = extra.Concat(page.Items.Where(x => !replacedFilms.Contains(x.Id) && !extra.Any(e => e.Id == x.Id)))
                    .OrderByDescending(x => x.UserData?.LastPlayedDate).ToArray();
                var limit = context.ActionArguments.TryGetValue("limit", out var limitArg) && limitArg is int n && n > 0 ? n : all.Length;
                page.Items = all.Take(limit).ToArray();
                page.TotalRecordCount += extra.Count - replaced;
            }
        }
    }

    private Movie? FindFilm(string partPath, string sourceId)
    {
        var dir = Path.GetDirectoryName(partPath);
        if (dir is null) return null;
        if (Path.GetFileName(dir) == ".hdrezka-parts") dir = Path.GetDirectoryName(dir);
        if (dir is null) return null;
        var path = Path.Combine(dir, LibraryPath.PrimaryFileName(sourceId));
        return library.GetItemById<Movie>(library.GetNewItemId(path, typeof(Movie)));
    }
}
