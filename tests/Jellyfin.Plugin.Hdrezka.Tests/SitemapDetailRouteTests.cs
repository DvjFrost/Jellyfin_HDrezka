using Jellyfin.Plugin.Hdrezka;
using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

public sealed class SitemapDetailRouteTests
{
    [Theory]
    [InlineData("GET", "UserLibrary", "GetItem", true)]
    [InlineData("GET", "UserLibrary", "GetItemLegacy", true)]
    [InlineData("GET", "Items", "GetItems", false)]
    [InlineData("POST", "UserLibrary", "GetItem", false)]
    [InlineData("GET", "UserLibrary", "GetLatestMedia", false)]
    public void RunsOnlyOnSingleItemDetails(string method, string controller, string action, bool expected)
        => Assert.Equal(expected, SitemapDetailRoute.IsDetail(method, controller, action));

    [Theory]
    [InlineData("GET", "TvShows", "GetEpisodes", true)]
    [InlineData("POST", "TvShows", "GetEpisodes", false)]
    [InlineData("GET", "TvShows", "GetSeasons", false)]
    [InlineData("GET", "Items", "GetItems", false)]
    public void RecognizesEpisodeList(string method, string controller, string action, bool expected)
        => Assert.Equal(expected, SitemapDetailRoute.IsEpisodeList(method, controller, action));

    [Fact]
    public async Task ApiKeyWithoutUserDoesNotTryToHydrate()
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("Jellyfin-UserId", Guid.Empty.ToString())], "test"));
        var descriptor = new ControllerActionDescriptor { ControllerName = "UserLibrary", ActionName = "GetItem" };
        var action = new ActionContext(http, new RouteData(), descriptor);
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?> { ["itemId"] = Guid.NewGuid() }, new object());
        var nextCalled = false;
        await new SitemapDetailFilter(null!, null!, null!, null!).OnActionExecutionAsync(context, () => {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(action, [], new object()));
        });
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("DynamicHls", "GetMasterHlsVideoPlaylist")]
    [InlineData("Videos", "GetVideoStream")]
    public async Task StreamingRequestsCarrySelectedMediaSource(string controller, string actionName)
    {
        var selected = Guid.NewGuid();
        var http = new DefaultHttpContext();
        var descriptor = new ControllerActionDescriptor { ControllerName = controller, ActionName = actionName };
        var action = new ActionContext(http, new RouteData(), descriptor);
        var context = new ActionExecutingContext(action, [], new Dictionary<string, object?> { ["itemId"] = Guid.NewGuid(), ["mediaSourceId"] = selected.ToString("N") }, new object());
        await new SitemapDetailFilter(null!, null!, null!, null!).OnActionExecutionAsync(context, () => {
            Assert.Equal(selected.ToString("N"), PlaybackSelection.Current);
            return Task.FromResult(new ActionExecutedContext(action, [], new object()));
        });
        Assert.Null(PlaybackSelection.Current);
    }
}
