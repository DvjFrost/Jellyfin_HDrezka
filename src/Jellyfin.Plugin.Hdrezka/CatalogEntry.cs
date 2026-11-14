namespace Jellyfin.Plugin.Hdrezka;

public enum CatalogKind { Movie, Series, Other, Cartoon, Anime, Show }
public sealed record CatalogEntry(string Title, Uri Url, Uri? Poster, string Description, CatalogKind Kind);
