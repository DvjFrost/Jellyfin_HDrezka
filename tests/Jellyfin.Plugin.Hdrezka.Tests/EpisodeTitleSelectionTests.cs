using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class EpisodeTitleSelectionTests
{
    [Fact]
    public void ReplacesGenericTitleButPreservesExistingMeaningfulTitle()
    {
        Assert.Equal("Пилот", EpisodeTitleSelection.Choose("Серия 1", "Пилот", 1));
        Assert.Equal("Пилот", EpisodeTitleSelection.Choose("Пилот", "Серия 1", 1));
        Assert.Equal("Моё название", EpisodeTitleSelection.Choose("Моё название", "Пилот", 1));
        Assert.True(EpisodeTitleSelection.ShouldRepair("Серия 1", "HDrezka-123 S01E01.strm", "Пилот", 1, false));
        Assert.False(EpisodeTitleSelection.ShouldRepair("Серия 1", "HDrezka-123 S01E01.strm", "Пилот", 1, true));
        Assert.False(EpisodeTitleSelection.ShouldRepair("Моё название", "HDrezka-123 S01E01.strm", "Пилот", 1, false));
        Assert.False(LibraryVersionTitles.ShouldSyncAlternateName("Моё название", "Пилот", true));
        Assert.True(LibraryVersionTitles.ShouldSyncAlternateName("Серия 1", "Пилот", false));
    }
}
