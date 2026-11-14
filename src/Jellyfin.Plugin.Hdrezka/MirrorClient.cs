using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Collections.Concurrent;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class MirrorAccessException(string message, int? statusCode = null) : Exception(message)
{
    public int? StatusCode { get; } = statusCode;
}

/// <summary>Mirror HTTP session. Cookies are scoped by the standard CookieContainer.</summary>
public sealed class MirrorClient : IDisposable
{
    public const string Agent = "Mozilla/5.0 (Linux; Android 17; samsung) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36";
    private readonly Uri _mirror;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly bool _sharedClient;
    private readonly TimeSpan _requestInterval;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static long _lastRequestTimestamp;
    private sealed class ChallengeState { public readonly SemaphoreSlim Gate = new(1, 1); public long Generation; }
    private static readonly ConcurrentDictionary<string, ChallengeState> Challenges = new(StringComparer.OrdinalIgnoreCase);
    private ChallengeState Challenge => Challenges.GetOrAdd(_mirror.GetLeftPart(UriPartial.Authority), static _ => new ChallengeState());
    private Uri _effective;

    public MirrorClient(Uri mirror, HttpClient? client = null, TimeSpan? requestInterval = null)
    {
        if (!mirror.IsAbsoluteUri || mirror.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(mirror.UserInfo)) throw new ArgumentException("Mirror must use HTTPS", nameof(mirror));
        _mirror = new Uri(mirror.GetLeftPart(UriPartial.Authority) + "/");
        _requestInterval = requestInterval ?? TimeSpan.Zero;
        if (_requestInterval < TimeSpan.Zero || _requestInterval > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(requestInterval));
        _effective = _mirror;
        if (client is null)
        {
            _client = MirrorSessionRegistry.Client(_mirror);
            _ownsClient = false;
            _sharedClient = true;
        }
        else _client = client;
    }

    public Uri CurrentMirror => _effective;

    public Task<string> GetPageAsync(Uri uri, CancellationToken cancellationToken) => GetAsync(uri, 2_000_000, cancellationToken);

    public Task<string> GetSitemapAsync(Uri uri, CancellationToken cancellationToken) => GetAsync(uri, 20_000_000, cancellationToken);

    private async Task<string> GetAsync(Uri uri, int maxLength, CancellationToken cancellationToken)
    {
        var state = Challenge;
        var generation = Interlocked.Read(ref state.Generation);
        var page = await SendAsync(HttpMethod.Get, uri, null, cancellationToken, maxLength: maxLength).ConfigureAwait(false);
        _effective = new Uri(page.Url.GetLeftPart(UriPartial.Authority) + "/");
        if (!page.Body.Contains("anubis_challenge", StringComparison.OrdinalIgnoreCase)) return EnsureSuccess(page);
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Interlocked.Read(ref state.Generation) != generation)
            {
                page = await SendAsync(HttpMethod.Get, page.Url, null, cancellationToken, maxLength: maxLength).ConfigureAwait(false);
                if (!ContainsChallenge(page.Body)) return EnsureSuccess(page);
            }
            var passed = await PassAnubisAsync(page.Body, page.Url, cancellationToken, maxLength).ConfigureAwait(false);
            Interlocked.Increment(ref state.Generation);
            if (passed.Url.AbsolutePath == page.Url.AbsolutePath && !ContainsChallenge(passed.Body)) return EnsureSuccess(passed);
            var retry = await SendAsync(HttpMethod.Get, page.Url, null, cancellationToken, maxLength: maxLength).ConfigureAwait(false);
            if (ContainsChallenge(retry.Body)) throw new MirrorAccessException("Зеркало повторно запросило проверку Anubis.");
            return EnsureSuccess(retry);
        }
        finally { state.Gate.Release(); }
    }

    public async Task<string> PostFormAsync(string relativePath, IReadOnlyDictionary<string,string> data, CancellationToken cancellationToken)
    {
        var uri = new Uri(_effective, relativePath.TrimStart('/'));
        var state = Challenge;
        var generation = Interlocked.Read(ref state.Generation);
        var response = await SendAsync(HttpMethod.Post, uri, data, cancellationToken).ConfigureAwait(false);
        if (ContainsChallenge(response.Body))
        {
            await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Interlocked.Read(ref state.Generation) != generation)
                    response = await SendAsync(HttpMethod.Post, uri, data, cancellationToken).ConfigureAwait(false);
                if (ContainsChallenge(response.Body))
                {
                    await PassAnubisAsync(response.Body, new Uri(response.Url.GetLeftPart(UriPartial.Authority) + "/"), cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref state.Generation);
                    response = await SendAsync(HttpMethod.Post, uri, data, cancellationToken).ConfigureAwait(false);
                    if (ContainsChallenge(response.Body)) throw new MirrorAccessException("Зеркало повторно запросило проверку Anubis.");
                }
            }
            finally { state.Gate.Release(); }
        }
        return EnsureSuccess(response);
    }

    private async Task<Page> PassAnubisAsync(string html, Uri redirect, CancellationToken token, int maxLength = 2_000_000)
    {
        var challenge = Anubis.TryParse(html) ?? throw new MirrorAccessException("Неподдерживаемая проверка Anubis.");
        var watch = Stopwatch.StartNew();
        var proof = await Task.Run(() => Anubis.Solve(challenge, TimeSpan.FromSeconds(10), token), token).ConfigureAwait(false)
            ?? throw new MirrorAccessException("Не удалось выполнить проверку Anubis за отведённое время.");
        var path = challenge.Prefix + "/.within.website/x/cmd/anubis/api/pass-challenge";
        var query = "id=" + Uri.EscapeDataString(challenge.Id) + "&response=" + proof.Hash + "&nonce=" + proof.Nonce.ToString(CultureInfo.InvariantCulture)
            + "&redir=" + Uri.EscapeDataString(redirect.AbsoluteUri) + "&elapsedTime=" + Math.Max(1, watch.ElapsedMilliseconds).ToString(CultureInfo.InvariantCulture);
        var pass = new Uri(redirect.GetLeftPart(UriPartial.Authority) + path + "?" + query);
        var response = await SendAsync(HttpMethod.Get, pass, null, token, redirect, maxLength).ConfigureAwait(false);
        if (ContainsChallenge(response.Body)) throw new MirrorAccessException("Сайт не подтвердил проверку Anubis.");
        return response;
    }

    private async Task<Page> SendAsync(HttpMethod method, Uri uri, IReadOnlyDictionary<string,string>? form, CancellationToken token, Uri? referer = null, int maxLength = 2_000_000)
    {
        for (var i = 0; i < 6; i++)
        {
            if (!IsAllowed(uri)) throw new MirrorAccessException("Зеркало перенаправило запрос на неподтверждённый адрес.");
            await WaitForRequestTurnAsync(token).ConfigureAwait(false);
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", Agent);
            if (uri.Host.Equals("hdrzk.org", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.TryAddWithoutValidation("X-Hdrezka-Android-App", "1");
                request.Headers.TryAddWithoutValidation("X-Hdrezka-Android-App-Version", "2.2.5");
            }
            if (referer is not null && referer.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)) request.Headers.Referrer = referer;
            if (method == HttpMethod.Post && form is not null) { request.Content = new FormUrlEncodedContent(form); request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest"); }
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.InternalServerError) throw new MirrorAccessException("Зеркало вернуло HTTP 500; обработка остановлена.", 500);
            if (_sharedClient && response.Headers.Contains("Set-Cookie"))
                await MirrorSessionRegistry.SaveAsync(_mirror, token).ConfigureAwait(false);
            if ((int)response.StatusCode is >= 300 and <= 399 && response.Headers.Location is not null)
            {
                var next = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                if (!IsAllowed(next)) throw new MirrorAccessException("Зеркало перенаправило запрос на неподтверждённый адрес.");
                if (method == HttpMethod.Post && (int)response.StatusCode is 301 or 302 or 303) { method = HttpMethod.Get; form = null; }
                uri = next; continue;
            }
            if (response.Content.Headers.ContentLength > maxLength) throw new MirrorAccessException("Ответ зеркала слишком большой.");
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (body.Length > maxLength) throw new MirrorAccessException("Ответ зеркала слишком большой.");
            return new Page(uri, (int)response.StatusCode, body);
        }
        throw new MirrorAccessException("Слишком много перенаправлений зеркала.");
    }

    private async Task WaitForRequestTurnAsync(CancellationToken token)
    {
        if (_requestInterval == TimeSpan.Zero) return;
        await RequestGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_lastRequestTimestamp != 0)
            {
                var remaining = _requestInterval - Stopwatch.GetElapsedTime(_lastRequestTimestamp);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token).ConfigureAwait(false);
            }
            _lastRequestTimestamp = Stopwatch.GetTimestamp();
        }
        finally { RequestGate.Release(); }
    }

    private bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Host.Equals(_mirror.Host, StringComparison.OrdinalIgnoreCase)) return true;
        return _mirror.Host.Equals("rezka-tv.org", StringComparison.OrdinalIgnoreCase) && uri.Host.Equals("hdrezka.la", StringComparison.OrdinalIgnoreCase);
    }
    private static bool ContainsChallenge(string body) => body.Contains("anubis_challenge", StringComparison.OrdinalIgnoreCase);
    private static string EnsureSuccess(Page page) => page.Status is >= 200 and < 300 ? page.Body : throw new MirrorAccessException("Зеркало вернуло HTTP " + page.Status.ToString(CultureInfo.InvariantCulture), page.Status);
    private sealed record Page(Uri Url, int Status, string Body);
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}







