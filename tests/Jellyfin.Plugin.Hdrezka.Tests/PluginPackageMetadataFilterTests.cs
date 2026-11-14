using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Model.Updates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

public class PluginPackageMetadataFilterTests
{
    [Fact]
    public async Task SuppliesLocalAuthorForOurMissingPackage()
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = "Package", ActionName = "GetPackageInfo" };
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var args = new Dictionary<string, object?> { ["name"] = "HDrezka", ["assemblyGuid"] = Guid.Parse("e92a7808-6dc0-45a0-ae64-6d19caa8e93a") };
        var context = new ActionExecutingContext(action, [], args, new object());
        var executed = new ActionExecutedContext(action, [], new object()) { Result = new NotFoundResult() };
        await new PluginPackageMetadataFilter().OnActionExecutionAsync(context, () => Task.FromResult(executed));
        var result = Assert.IsType<OkObjectResult>(executed.Result);
        var info = Assert.IsType<PackageInfo>(result.Value);
        Assert.Equal("Frost & Codex", info.Owner);
    }
    [Fact]
    public async Task UsesOurAuthorForExistingPackageWithoutChangingItsVersions()
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = "Package", ActionName = "GetPackageInfo" };
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?> { ["name"] = "HDrezka", ["assemblyGuid"] = Guid.Parse("e92a7808-6dc0-45a0-ae64-6d19caa8e93a") }, new object());
        var info = new PackageInfo { Id = Guid.Parse("e92a7808-6dc0-45a0-ae64-6d19caa8e93a"), Name = "HDrezka", Owner = "Old", Versions = [new VersionInfo { Version = "0.1.0.0" }] };
        var executed = new ActionExecutedContext(action, [], new object()) { Result = new OkObjectResult(info) };
        await new PluginPackageMetadataFilter().OnActionExecutionAsync(context, () => Task.FromResult(executed));
        Assert.Equal("Frost & Codex", info.Owner);
        Assert.Single(info.Versions);
    }
    [Fact]
    public async Task LeavesUnrelatedMissingPackageAlone()
    {
        var descriptor = new ControllerActionDescriptor { ControllerName = "Package", ActionName = "GetPackageInfo" };
        var action = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?> { ["name"] = "Other" }, new object());
        var executed = new ActionExecutedContext(action, [], new object()) { Result = new NotFoundResult() };
        await new PluginPackageMetadataFilter().OnActionExecutionAsync(context, () => Task.FromResult(executed));
        Assert.IsType<NotFoundResult>(executed.Result);
    }
}
