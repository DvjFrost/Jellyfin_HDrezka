using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Hdrezka;

[ApiController]
[Route("Hdrezka/Session")]
[Authorize(Policy = "RequiresElevation")]
public sealed class MirrorSessionController : ControllerBase
{
    [HttpPost("Reset")]
    public async Task<IActionResult> Reset(CancellationToken token)
    {
        var url = Plugin.Instance?.Configuration.MirrorUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var mirror) || mirror.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(mirror.UserInfo))
            return BadRequest();
        await MirrorClient.ResetSessionAsync(mirror, token).ConfigureAwait(false);
        return NoContent();
    }
}
