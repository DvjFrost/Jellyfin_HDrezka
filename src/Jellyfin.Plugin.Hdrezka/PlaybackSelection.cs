namespace Jellyfin.Plugin.Hdrezka;

public static class PlaybackSelection
{
    private static readonly AsyncLocal<string?> Selection = new();
    public static string? Current { get => Selection.Value; set => Selection.Value = value; }

    public static string Resolve(string? queryValue, object? body, Guid itemId)
    {
        var bodyValue = body?.GetType().GetProperty("MediaSourceId")?.GetValue(body)?.ToString();
        return Guid.TryParse(queryValue, out var query) && query != Guid.Empty ? query.ToString("N")
            : Guid.TryParse(bodyValue, out var value) && value != Guid.Empty ? value.ToString("N")
            : itemId.ToString("N");
    }
}
