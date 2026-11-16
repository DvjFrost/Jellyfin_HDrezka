using MediaBrowser.Controller.Drawing;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Hdrezka;

// Jellyfin's landscape resume cards deliberately ignore a series poster when
// the episode has no frame. Supply it as a virtual inherited thumbnail only
// for HDrezka episodes; no image files or real thumbnail metadata are changed.
public sealed class ResumePosterFallbackFilter(ILibraryManager library, LibraryMediaProvider media, IImageProcessor images) : IAsyncActionFilter
{
    public static bool NeedsFallback(BaseItemDto item) =>
        item.Type == BaseItemKind.Episode && item.SeriesId.HasValue && !string.IsNullOrEmpty(item.SeriesPrimaryImageTag)
        && !(item.ImageTags?.ContainsKey(ImageType.Primary) ?? false)
        && !(item.ImageTags?.ContainsKey(ImageType.Thumb) ?? false)
        && item.ParentThumbItemId is null;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        if (action?.ControllerName == "Image" && action.ActionName is "GetItemImage" or "GetItemImageByIndex"
            && context.ActionArguments.TryGetValue("itemId", out var imageId) && imageId is Guid seriesId
            && context.ActionArguments.TryGetValue("imageType", out var type) && type is ImageType.Thumb
            && context.ActionArguments.TryGetValue("tag", out var tagArg) && tagArg is string tag
            && library.GetItemById<Series>(seriesId) is { Path: not null } series
            && LibrarySeriesLocator.Load(media.Root, series.Path) is not null
            && series.GetImageInfo(ImageType.Thumb, 0) is null
            && series.GetImageInfo(ImageType.Primary, 0) is { } poster
            && string.Equals(images.GetImageCacheTag(series, poster), tag, StringComparison.OrdinalIgnoreCase))
            context.ActionArguments["imageType"] = ImageType.Primary;

        var result = await next().ConfigureAwait(false);
        if (action?.ControllerName != "Items" || action.ActionName is not ("GetResumeItems" or "GetResumeItemsLegacy")
            || result.Result is not ObjectResult { Value: QueryResult<BaseItemDto> page }) return;
        foreach (var item in page.Items)
        {
            if (!NeedsFallback(item) || library.GetItemById<Series>(item.SeriesId!.Value) is not { Path: not null } parentSeries
                || LibrarySeriesLocator.Load(media.Root, parentSeries.Path) is null
                || parentSeries.GetImageInfo(ImageType.Thumb, 0) is not null) continue;
            item.ParentThumbItemId = item.SeriesId;
            item.ParentThumbImageTag = item.SeriesPrimaryImageTag;
        }
    }
}
