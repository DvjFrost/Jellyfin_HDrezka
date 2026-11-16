using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Hdrezka;

// Jellyfin normally validates every child of a series when identifying it.
// HDrezka exposes one local episode version per voice; propagating the
// selected series search result to all of them triggers thousands of lookups.
public sealed class SeriesIdentifyFilter(ILibraryManager library, IUserManager users, LibraryMediaProvider media) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (action?.ControllerName != "ItemLookup" || action.ActionName != "ApplySearchCriteria"
            || !context.ActionArguments.TryGetValue("itemId", out var idArg) || idArg is not Guid id
            || !context.ActionArguments.TryGetValue("searchResult", out var resultArg) || resultArg is not RemoteSearchResult result
            || !Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var userId)
            || users.GetUserById(userId) is not { } user
            || library.GetItemById<Series>(id, user) is not { Path: not null } item
            || LibrarySeriesLocator.Load(media.Root, item.Path) is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        var replaceImages = !context.ActionArguments.TryGetValue("replaceAllImages", out var value) || value is not bool flag || flag;
        item.SetProviderIds(result.ProviderIds);
        await item.RefreshMetadata(new MetadataRefreshOptions(new DirectoryService(BaseItem.FileSystem))
        {
            MetadataRefreshMode = MetadataRefreshMode.FullRefresh,
            ImageRefreshMode = MetadataRefreshMode.FullRefresh,
            ReplaceAllMetadata = true,
            ReplaceAllImages = replaceImages,
            SearchResult = result,
            RemoveOldMetadata = true
        }, context.HttpContext.RequestAborted).ConfigureAwait(false);
        context.Result = new NoContentResult();
    }
}
