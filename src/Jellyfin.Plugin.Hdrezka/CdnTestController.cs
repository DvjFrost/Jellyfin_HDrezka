using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Hdrezka;

[ApiController]
[Route("Hdrezka/Cdn")]
[Authorize(Policy = "RequiresElevation")]
public sealed class CdnTestController : ControllerBase
{
    private static (Uri Source, Uri Referer, DateTime Expiry)? _sample;
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public static void Remember(Uri? source, Uri referer)
    {
        if (source is not null && source.Host.Equals("stream.voidboost.one", StringComparison.OrdinalIgnoreCase))
            _sample = (source, referer, DateTime.UtcNow.AddMinutes(2));
    }

    [HttpPost("Test")]
    public async Task<IActionResult> Test(CancellationToken token)
    {
        var sample = _sample;
        if (sample is null || sample.Value.Expiry <= DateTime.UtcNow) return BadRequest(new { Message = "Сначала откройте карточку видео, затем в течение двух минут запустите проверку." });
        var results = new List<CdnTestResult>();
        foreach (var host in new[] { "" }.Concat(CdnSelection.Hosts))
        {
            token.ThrowIfCancellationRequested();
            var url = CdnSelection.Apply(sample.Value.Source, host);
            var watch = Stopwatch.StartNew();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(6));
                var originHost = url.Host;
                for (var redirectCount = 0; ; redirectCount++)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new RangeHeaderValue(0, 4095);
                    request.Headers.Referrer = sample.Value.Referer;
                    request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 17; samsung) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                    if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is { } location)
                    {
                        var next = location.IsAbsoluteUri ? location : new Uri(url, location);
                        if (redirectCount >= 3 || next.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(next.UserInfo) || !(next.Host.Equals(originHost, StringComparison.OrdinalIgnoreCase) || next.Host.EndsWith("." + originHost, StringComparison.OrdinalIgnoreCase)))
                        { results.Add(new(host, false, null, "Неподдерживаемое перенаправление")); break; }
                        url = next;
                        continue;
                    }
                    if (response.StatusCode != System.Net.HttpStatusCode.PartialContent) { results.Add(new(host, false, null, $"HTTP {(int)response.StatusCode}")); break; }
                    await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                    var buffer = new byte[4096];
                    var read = await body.ReadAsync(buffer, timeout.Token);
                    results.Add(new(host, read > 0, watch.ElapsedMilliseconds, read > 0 ? null : "Пустой ответ"));
                    break;
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { results.Add(new(host, false, null, "Время ожидания истекло")); }
            catch (HttpRequestException) { results.Add(new(host, false, null, "Ошибка соединения")); }
        }
        return Ok(results);
    }
    public sealed record CdnTestResult(string Host, bool Available, long? Milliseconds, string? Error);
}

