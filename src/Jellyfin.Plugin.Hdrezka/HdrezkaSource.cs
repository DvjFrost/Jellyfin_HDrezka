using System.Text.Json;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record NamedStream(string Voice, VideoStream Stream);
public sealed record VoiceListing(string Title, IReadOnlyList<FreeVoice> Voices);
public sealed record VideoDetailListing(CatalogDetail Detail, IReadOnlyList<FreeVoice> Voices, string? ActiveVoiceId = null, IReadOnlyList<Episode>? EmbeddedEpisodes = null, IReadOnlyDictionary<(int Season, int Episode), string>? EpisodeNames = null);

public sealed class HdrezkaSource(MirrorClient mirror)
{
    private readonly Dictionary<string, VoiceListing> _sessionListings = new(StringComparer.Ordinal);

    public async Task<VideoDetailListing> DetailAndVoicesAsync(string path, CancellationToken token)
    {
        var id = ContentId(path);
        var html = await mirror.GetPageAsync(new Uri(mirror.CurrentMirror, path), token).ConfigureAwait(false);
        var detail = DetailParser.Parse(html, mirror.CurrentMirror) ?? throw new MirrorAccessException("Зеркало вернуло страницу без карточки видео.");
        var voices = VoiceParser.ParseFree(html, id).Where(v => v.ContentId.Length == 0 || v.ContentId == id).ToArray();
        if (voices.Length > 0) _sessionListings[path] = new VoiceListing(detail.Title, voices);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var active = doc.DocumentNode.SelectSingleNode("//*[contains(concat(' ',normalize-space(@class),' '),' b-translator__item ') and contains(concat(' ',normalize-space(@class),' '),' active ')]");
        var activeId = active?.GetAttributeValue("data-translator_id", active.GetAttributeValue("data-translator-id", ""));
        var episodes = detail.HasEpisodes ? EpisodeParser.Parse(html) : [];
        var episodeNames = detail.HasEpisodes ? EpisodeScheduleParser.Parse(html) : null;
        return new VideoDetailListing(detail, voices, activeId, episodes, episodeNames);
    }

    public async Task<CatalogDetail> DetailAsync(string path, CancellationToken token)
    {
        _ = ItemKey.EncodePath(path);
        var html = await mirror.GetPageAsync(new Uri(mirror.CurrentMirror, path), token).ConfigureAwait(false);
        return DetailParser.Parse(html, mirror.CurrentMirror) ?? throw new MirrorAccessException("Зеркало вернуло страницу без карточки видео.");
    }

    public async Task<IReadOnlyList<FreeVoice>> VoicesAsync(string path, CancellationToken token)
        => (await VoiceListingAsync(path, token).ConfigureAwait(false)).Voices;

    public async Task<VoiceListing> VoiceListingAsync(string path, CancellationToken token)
    {
        if (_sessionListings.TryGetValue(path, out var cached)) return cached;
        var id = ContentId(path);
        var html = await mirror.GetPageAsync(new Uri(mirror.CurrentMirror, path), token).ConfigureAwait(false);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var title = HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//h1")?.InnerText ?? "").Trim();
        var voices = VoiceParser.ParseFree(html, id).Where(v => v.ContentId.Length == 0 || v.ContentId == id).ToArray();
        var listing = new VoiceListing(title, voices);
        if (voices.Length > 0) _sessionListings[path] = listing;
        return listing;
    }

    public async Task<IReadOnlyList<NamedStream>> MovieStreamsAsync(string path, CancellationToken token)
    {
        var id = ContentId(path);
        var voices = await VoicesAsync(path, token).ConfigureAwait(false);
        var result = new List<NamedStream>();
        foreach (var voice in voices)
        {
            token.ThrowIfCancellationRequested();
            var form = new Dictionary<string,string> { ["id"] = id, ["translator_id"] = voice.Id, ["is_camrip"] = voice.IsCamrip, ["is_ads"] = voice.IsAds, ["is_director"] = voice.IsDirector, ["action"] = "get_movie" };
            var json = await mirror.PostFormAsync("ajax/get_cdn_series/", form, token).ConfigureAwait(false);
            result.AddRange(ParseStreams(json).Select(stream => new NamedStream(voice.Title, stream)));
        }
        return result;
    }

    public async Task<IReadOnlyList<NamedStream>> MovieBestStreamsAsync(string path, CancellationToken token)
    {
        var voices = await VoicesAsync(path, token).ConfigureAwait(false);
        foreach (var voice in voices)
        {
            var candidates = await MovieVoiceStreamsAsync(path, voice.Id, token).ConfigureAwait(false);
            var best = candidates.Where(x => x.Stream.Quality <= 1080).OrderByDescending(x => x.Stream.Quality).FirstOrDefault();
            if (best is not null) return [best];
        }
        return Array.Empty<NamedStream>();
    }
    public async Task<IReadOnlyList<NamedStream>> MovieVoiceStreamsAsync(string path, string voiceId, CancellationToken token)
    {
        var voice = (await VoicesAsync(path, token).ConfigureAwait(false)).FirstOrDefault(v => v.Id == voiceId);
        if (voice is null) return Array.Empty<NamedStream>();
        return await MovieVoiceStreamsFromListingAsync(path, voice, token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<NamedStream>> MovieVoiceStreamsFromListingAsync(string path, FreeVoice voice, CancellationToken token)
        => await MovieVoiceStreamsCoreAsync(path, voice, false, token).ConfigureAwait(false);

    public async Task<IReadOnlyList<NamedStream>> MovieVoiceAlternativesFromListingAsync(string path, FreeVoice voice, CancellationToken token)
        => await MovieVoiceStreamsCoreAsync(path, voice, true, token).ConfigureAwait(false);

    private async Task<IReadOnlyList<NamedStream>> MovieVoiceStreamsCoreAsync(string path, FreeVoice voice, bool alternatives, CancellationToken token)
    {
        var id = ContentId(path);
        if (!Digits(voice.Id) || (voice.ContentId.Length > 0 && voice.ContentId != id)) return [];
        var form = new Dictionary<string,string> { ["id"] = id, ["translator_id"] = voice.Id, ["is_camrip"] = voice.IsCamrip, ["is_ads"] = voice.IsAds, ["is_director"] = voice.IsDirector, ["action"] = "get_movie" };
        var json = await mirror.PostFormAsync("ajax/get_cdn_series/", form, token).ConfigureAwait(false);
        return ParseStreams(json, alternatives).Select(stream => new NamedStream(voice.Title, stream)).ToArray();
    }
    public async Task<IReadOnlyList<Episode>> EpisodesAsync(string path, string voiceId, CancellationToken token)
    {
        var id = ContentId(path);
        var voices = await VoicesAsync(path, token).ConfigureAwait(false);
        if (!voices.Any(v => v.Id == voiceId)) return Array.Empty<Episode>();
        return await FetchEpisodesAsync(id, voiceId, token).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Episode>> EpisodesFromListingAsync(string path, VideoDetailListing listing, FreeVoice voice, CancellationToken token)
    {
        var id = ContentId(path);
        if (!listing.Voices.Contains(voice) || (voice.ContentId.Length > 0 && voice.ContentId != id)) return [];
        var episodes = listing.EmbeddedEpisodes is { Count: > 0 } && string.Equals(listing.ActiveVoiceId, voice.Id, StringComparison.Ordinal)
            ? listing.EmbeddedEpisodes : await FetchEpisodesAsync(id, voice.Id, token).ConfigureAwait(false);
        return EpisodeScheduleParser.Apply(episodes, listing.EpisodeNames);
    }

    private async Task<IReadOnlyList<Episode>> FetchEpisodesAsync(string id, string voiceId, CancellationToken token)
    {
        var form = new Dictionary<string,string> { ["id"] = id, ["translator_id"] = voiceId, ["action"] = "get_episodes" };
        var json = await mirror.PostFormAsync("ajax/get_cdn_series/", form, token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!Success(doc.RootElement) || IsPremium(doc.RootElement) || !doc.RootElement.TryGetProperty("episodes", out var episodes) || episodes.ValueKind != JsonValueKind.String) return Array.Empty<Episode>();
        return EpisodeParser.Parse(episodes.GetString() ?? "");
    }

    public async Task<IReadOnlyList<NamedStream>> EpisodeStreamsAsync(string path, string voiceId, string season, string episode, CancellationToken token)
    {
        var voice = (await VoicesAsync(path, token).ConfigureAwait(false)).FirstOrDefault(v => v.Id == voiceId);
        if (voice is null) return Array.Empty<NamedStream>();
        return await EpisodeStreamsFromListingAsync(path, voice, season, episode, token).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<NamedStream>> EpisodeStreamsFromListingAsync(string path, FreeVoice voice, string season, string episode, CancellationToken token)
        => await EpisodeStreamsCoreAsync(path, voice, season, episode, false, token).ConfigureAwait(false);

    public async Task<IReadOnlyList<NamedStream>> EpisodeAlternativesFromListingAsync(string path, FreeVoice voice, string season, string episode, CancellationToken token)
        => await EpisodeStreamsCoreAsync(path, voice, season, episode, true, token).ConfigureAwait(false);

    private async Task<IReadOnlyList<NamedStream>> EpisodeStreamsCoreAsync(string path, FreeVoice voice, string season, string episode, bool alternatives, CancellationToken token)
    {
        var id = ContentId(path);
        if (!Digits(season) || !Digits(episode) || !Digits(voice.Id) || (voice.ContentId.Length > 0 && voice.ContentId != id)) return [];
        var form = new Dictionary<string,string> { ["id"] = id, ["translator_id"] = voice.Id, ["season"] = season, ["episode"] = episode, ["action"] = "get_stream" };
        var json = await mirror.PostFormAsync("ajax/get_cdn_series/", form, token).ConfigureAwait(false);
        return ParseStreams(json, alternatives).Select(stream => new NamedStream(voice.Title, stream)).ToArray();
    }

    private static IReadOnlyList<VideoStream> ParseStreams(string json, bool alternatives = false)
    {
        using var doc = JsonDocument.Parse(json);
        if (!Success(doc.RootElement) || IsPremium(doc.RootElement) || !doc.RootElement.TryGetProperty("url", out var url) || url.ValueKind != JsonValueKind.String) return Array.Empty<VideoStream>();
        return alternatives ? StreamParser.ParseAlternatives(url.GetString() ?? "") : StreamParser.Parse(url.GetString() ?? "");
    }
    private static bool Success(JsonElement root) => root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
    private static bool IsPremium(JsonElement root)
    {
        if (!root.TryGetProperty("premium_content", out var value)) return false;
        return value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n != 0)
            || (value.ValueKind == JsonValueKind.String && value.GetString() is "1" or "true");
    }
    private static bool Digits(string value) => value.Length is >= 1 and <= 8 && value.All(char.IsAsciiDigit);
    private static string ContentId(string path)
    {
        _ = ItemKey.EncodePath(path);
        var match = Regex.Match(path, @"/(\d{1,16})-[^/]+$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : throw new ArgumentException("Invalid content path", nameof(path));
    }
}







