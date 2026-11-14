using Jellyfin.Plugin.Hdrezka;
using Xunit;
using MediaBrowser.Controller.Entities.Movies;

public sealed class LibraryVersionTitleTests
{
    [Fact]
    public void RepairsOnlyGeneratedNamesOfAlternateFiles()
    {
        Assert.True(LibraryVersionTitles.ShouldRepair("HDrezka-123 S01E01 - voice-3", "HDrezka-123 S01E01 - voice-3.strm", "Серия 1"));
        Assert.False(LibraryVersionTitles.ShouldRepair("Моё название", "HDrezka-123 S01E01 - voice-3.strm", "Серия 1"));
        Assert.False(LibraryVersionTitles.ShouldRepair("Other", "Other.strm", "Серия 1"));
        Assert.False(LibraryVersionTitles.ShouldRepair("HDrezka-123 S01E01 - voice-3", "HDrezka-123 S01E01 - voice-3.strm", ""));
    }

    [Fact]
    public void RepairsShortcutFlagOnlyForGeneratedHdrezkaFiles()
    {
        var alternate = new Movie { Path = "HDrezka-123 - voice-3.strm", IsShortcut = false };
        Assert.True(LibraryVersionTitles.RepairShortcut(alternate));
        Assert.True(alternate.IsShortcut);
        Assert.False(LibraryVersionTitles.RepairShortcut(alternate));
        var unrelated = new Movie { Path = "Other.strm", IsShortcut = false };
        Assert.False(LibraryVersionTitles.RepairShortcut(unrelated));
        Assert.False(unrelated.IsShortcut);
    }
}
