using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Hdrezka;

public static class SitemapLibraries
{
    public static CatalogKind? OwnedKind(string name, IReadOnlyList<string> locations, string root)
    {
        var section = name switch { "Фильмы HDrezka" => "films", "Сериалы HDrezka" => "series", _ => null };
        if (section is null || locations.Count != 1) return null;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(locations[0])), Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, section))), comparison)
            ? section == "films" ? CatalogKind.Movie : CatalogKind.Series : null;
    }

    public static void ConfigureNfoSaver(LibraryOptions options)
        => options.MetadataSavers = (options.MetadataSavers ?? []).Where(x => !string.Equals(x, "Nfo", StringComparison.OrdinalIgnoreCase))
            .Append(HdrezkaNfoSaver.SaverName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static void ConfigureMetadata(LibraryOptions options, CatalogKind kind)
    {
        options.EnableInternetProviders = true;
        options.SaveLocalMetadata = true;
        ConfigureNfoSaver(options);
        var types = (options.TypeOptions ?? []).ToList();
        TypeOptions For(string type)
        {
            var setting = types.FirstOrDefault(x => string.Equals(x.Type, type, StringComparison.OrdinalIgnoreCase));
            if (setting is null) { setting = new TypeOptions { Type = type }; types.Add(setting); }
            return setting;
        }
        var target = For(kind == CatalogKind.Series ? "Series" : "Movie");
        target.MetadataFetchers = (target.MetadataFetchers ?? []).Append("TheMovieDb").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target.MetadataFetcherOrder = (target.MetadataFetcherOrder ?? []).Append("TheMovieDb").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target.ImageFetchers = (target.ImageFetchers ?? []).Append("TheMovieDb").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target.ImageFetcherOrder = (target.ImageFetcherOrder ?? []).Append("TheMovieDb").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (kind == CatalogKind.Series)
            foreach (var child in new[] { For("Season"), For("Episode") })
            {
                if (child.MetadataFetchers is not { Length: > 0 }) child.MetadataFetchers = ["HDrezka"];
                if (child.ImageFetchers is not { Length: > 0 }) child.ImageFetchers = ["HDrezka"];
            }
        options.TypeOptions = types.ToArray();
    }

    public static bool IsObsoleteOwnedLibrary(string name, IReadOnlyList<string> locations, string root)
    {
        var directory = name switch { "Мультфильмы HDrezka" => "cartoons", "Аниме HDrezka" => "animation", "ТВ-шоу HDrezka" => "show", _ => null };
        if (directory is null || locations.Count != 1) return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(locations[0]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(Path.Combine(root, directory)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison);
    }

    public static async Task<IReadOnlyDictionary<CatalogKind,Guid>> EnsureAsync(ILibraryManager library, string root, CancellationToken token)
    {
        foreach (var obsolete in library.GetVirtualFolders().Where(x => IsObsoleteOwnedLibrary(x.Name, x.Locations, root)).ToArray())
        {
            token.ThrowIfCancellationRequested();
            await library.RemoveVirtualFolder(obsolete.Name, false).ConfigureAwait(false);
        }
        var sections = new[]
        {
            (Kind: CatalogKind.Movie, Name: "Фильмы HDrezka", Directory: "films", Type: CollectionTypeOptions.movies),
            (Kind: CatalogKind.Series, Name: "Сериалы HDrezka", Directory: "series", Type: CollectionTypeOptions.tvshows)
        };
        var ids = new Dictionary<CatalogKind,Guid>();
        foreach (var section in sections)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.Combine(root, section.Directory);
            Directory.CreateDirectory(path);
            var folder = library.GetVirtualFolders().FirstOrDefault(x => string.Equals(x.Name, section.Name, StringComparison.OrdinalIgnoreCase));
            if (folder is not null && !folder.Locations.Any(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                throw new InvalidOperationException($"Медиатека «{section.Name}» уже существует и использует другую папку.");
            if (folder is null)
            {
                var options = new LibraryOptions { PathInfos = [new MediaPathInfo(path)], EnableRealtimeMonitor = false,
                    SaveLocalMetadata = true, MetadataSavers = ["Nfo"],
                    TypeOptions = new[] { "Movie", "Series", "Season", "Episode" }.Select(type => new TypeOptions { Type = type,
                        MetadataFetchers = ["HDrezka"], ImageFetchers = ["HDrezka"] }).ToArray() };
                ConfigureMetadata(options, section.Kind);
                await library.AddVirtualFolder(section.Name, section.Type, options, false).ConfigureAwait(false);
                folder = library.GetVirtualFolders().FirstOrDefault(x => string.Equals(x.Name, section.Name, StringComparison.OrdinalIgnoreCase));
            }
            if (folder is null || !Guid.TryParse(folder.ItemId, out var id)) throw new InvalidOperationException($"Не удалось создать медиатеку «{section.Name}».");
            if (library.GetItemById<CollectionFolder>(id) is { } collection)
            {
                var options = collection.GetLibraryOptions();
                var before = System.Text.Json.JsonSerializer.Serialize(options);
                ConfigureMetadata(options, section.Kind);
                if (before != System.Text.Json.JsonSerializer.Serialize(options)) collection.UpdateLibraryOptions(options);
            }
            ids.Add(section.Kind, id);
        }
        return ids;
    }

    public static async Task ScanAsync(ILibraryManager library, IReadOnlyDictionary<CatalogKind,Guid> ids, CancellationToken token)
    {
        foreach (var id in ids.Values)
            if (library.GetItemById(id) is Folder folder) await folder.ValidateChildren(new Progress<double>(), token).ConfigureAwait(false);
    }
}
