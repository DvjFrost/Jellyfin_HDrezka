using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Model.Entities;
using Xunit;

public sealed class DownloadLibrariesTests
{
    [Fact]
    public void ExcludesPluginLibrariesAndResolvesIndividualLocationsByStableIdentifier()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hdrezka-catalog"));
        var moviePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "local-movies"));
        var folder = new VirtualFolderInfo { Name = "Мои фильмы", ItemId = Guid.NewGuid().ToString("N"), CollectionType = CollectionTypeOptions.movies, Locations = [moviePath, Path.Combine(root, "films")] };
        var destinations = DownloadLibraries.List([folder], root);
        var destination = Assert.Single(destinations);
        Assert.Equal(moviePath, destination.Path);
        Assert.Equal(moviePath, DownloadLibraries.Resolve([folder], root, destination.Id, false));
        Assert.Null(DownloadLibraries.Resolve([folder], root, destination.Id, true));
        Assert.Null(DownloadLibraries.Resolve([], root, destination.Id, false));
    }

    [Fact]
    public void DoesNotExposeParentOfPluginCatalog()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "catalog-parent"));
        var folder = new VirtualFolderInfo { Name = "Unsafe", ItemId = Guid.NewGuid().ToString("N"), CollectionType = CollectionTypeOptions.movies, Locations = [parent] };
        Assert.Empty(DownloadLibraries.List([folder], Path.Combine(parent, "plugin")));
    }

    [Fact]
    public void NewlyCreatedLibraryIsAvailableBeforeJellyfinAssignsItsItemId()
    {
        var folder = new VirtualFolderInfo { Name = "Новая", CollectionType = CollectionTypeOptions.movies, Locations = [Path.Combine(Path.GetTempPath(), "new-movies")] };
        var root = Path.Combine(Path.GetTempPath(), "catalog");
        var before = Assert.Single(DownloadLibraries.List([folder], root));
        folder.ItemId = Guid.NewGuid().ToString("N");
        var after = Assert.Single(DownloadLibraries.List([folder], root));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.Path, DownloadLibraries.Resolve([folder], root, before.Id, false));
    }
}
