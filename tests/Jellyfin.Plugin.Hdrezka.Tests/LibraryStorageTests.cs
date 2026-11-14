using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class LibraryStorageTests
{
    [Fact]
    public void UsesConfiguredAbsoluteFolderOrServerScopedDefault()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hdrezka-test"));
        var data = Path.Combine(parent, "server");
        Assert.Equal(Path.Combine(data, "hdrezka-library"), LibraryStorage.ResolveRoot(data, ""));
        Assert.NotEqual(LibraryStorage.ResolveRoot(data, ""), LibraryStorage.ResolveRoot(Path.Combine(parent, "another-server"), ""));
        Assert.Equal(Path.Combine(parent, "hdrezka-library"), LibraryStorage.ResolveRoot(data, "", [Path.Combine(parent, "hdrezka-library", "films")]));
        Assert.Equal(Path.Combine(data, "hdrezka-library"), LibraryStorage.ResolveRoot(data, "", [Path.Combine(parent, "unrelated", "films")]));
        var selected = Path.Combine(parent, "selected");
        Assert.Equal(selected, LibraryStorage.ResolveRoot(data, selected));
        Assert.Throws<ArgumentException>(() => LibraryStorage.ResolveRoot(data, "relative-folder"));
    }
}
