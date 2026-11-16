using System.Collections;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Hdrezka;

// Jellyfin Web prefers a main-menu page over the settings page when both
// belong to the same plugin. Keep the menu query intact and make the
// unfiltered plugin-details query select the settings page instead.
public sealed class DashboardConfigurationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var unfiltered = action?.ControllerName == "Dashboard" && action.ActionName == "GetConfigurationPages"
            && (!context.ActionArguments.TryGetValue("enableInMainMenu", out var value) || value is null);
        var result = await next().ConfigureAwait(false);
        if (!unfiltered || result.Result is not ObjectResult { Value: IEnumerable pages }) return;
        foreach (var page in pages)
        {
            if (page is null) continue;
            var type = page.GetType();
            if (type.GetProperty("PluginId")?.GetValue(page) is not Guid id || id != Plugin.Instance?.Id
                || !string.Equals(type.GetProperty("Name")?.GetValue(page) as string, "HDrezkaDownloads", StringComparison.Ordinal)) continue;
            type.GetProperty("EnableInMainMenu")?.SetValue(page, false);
        }
    }
}
