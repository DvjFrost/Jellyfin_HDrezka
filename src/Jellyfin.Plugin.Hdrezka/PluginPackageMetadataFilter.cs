using MediaBrowser.Model.Updates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Hdrezka;

// Jellyfin's details page gets the developer from repository package metadata.
// For a manually installed plugin, supply only our own metadata when no repository has it.
public sealed class PluginPackageMetadataFilter : IAsyncActionFilter
{
    private static readonly Guid PluginId = Guid.Parse("e92a7808-6dc0-45a0-ae64-6d19caa8e93a");
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var action = context.ActionDescriptor as ControllerActionDescriptor;
        var matches = action?.ControllerName == "Package" && action.ActionName == "GetPackageInfo"
            && context.ActionArguments.TryGetValue("name", out var name) && string.Equals(name as string, "HDrezka", StringComparison.OrdinalIgnoreCase)
            && (!context.ActionArguments.TryGetValue("assemblyGuid", out var guid) || guid is null || guid is Guid id && id == PluginId);
        var result = await next().ConfigureAwait(false);
        if (!matches) return;
        if (result.Result is ObjectResult { Value: PackageInfo existing } && existing.Id == PluginId)
        {
            existing.Owner = "Frost & Codex";
            return;
        }
        if (result.Result is not NotFoundResult) return;
        result.Result = new OkObjectResult(new PackageInfo
        {
            Id = PluginId,
            Name = "HDrezka",
            Owner = "Frost & Codex",
            Description = "Каталог фильмов и сериалов HDrezka с данными TMDB и бесплатными озвучками.",
            Overview = "Каталог и воспроизведение HDrezka",
            Category = "Media",
            Versions = [new VersionInfo { Version = (Plugin.Instance?.Version ?? new Version(0, 1, 0, 0)).ToString(), TargetAbi = "12.4.0.0", RepositoryName = "Локальная установка" }]
        });
    }
}
