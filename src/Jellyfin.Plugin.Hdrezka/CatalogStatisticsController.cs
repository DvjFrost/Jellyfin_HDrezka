using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Hdrezka;

[ApiController]
[Route("Hdrezka/Catalog")]
[Authorize(Policy = "RequiresElevation")]
public sealed class CatalogStatisticsController(IApplicationPaths paths) : ControllerBase
{
    [HttpGet("Statistics")]
    public async Task<ActionResult<CatalogStatistics>> Get(CancellationToken token)
    {
        var directory = Path.Combine(paths.DataPath, "hdrezka-catalog");
        var path = Path.Combine(directory, "sitemap.json");
        if (!System.IO.File.Exists(path)) return Ok(new CatalogStatistics(0, 0, 0, 0, 0, 0));
        var videos = JsonSerializer.Deserialize<List<SitemapVideo>>(await System.IO.File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? [];
        var matches = await new TmdbMatchStore(Path.Combine(directory, "tmdb-matches.jsonl")).LoadAsync(token).ConfigureAwait(false);
        return Ok(CatalogStatistics.Calculate(videos, matches));
    }
}
