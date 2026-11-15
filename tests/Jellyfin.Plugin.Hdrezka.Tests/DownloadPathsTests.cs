using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class DownloadPathsTests
{
    [Fact]
    public void FormatsMovieAndEpisodePaths()
    {
        Assert.Equal("Одиссея (2026).mp4", DownloadPaths.Movie("Одиссея", 2026));
        Assert.Equal(Path.Combine("Доктор Хаус (Дубляж)", "Сезон 01", "S01E02 Название серии.mp4"),
            DownloadPaths.Episode("Доктор Хаус", "Дубляж", 1, 2, "Название серии"));
        Assert.Equal(Path.Combine("Доктор Хаус (Дубляж)", "Сезон 01", "S01E02 Серия 2.mp4"),
            DownloadPaths.Episode("Доктор Хаус", "Дубляж", 1, 2, null));
    }

    [Fact]
    public void RejectsTraversalAndWindowsReservedNamesInEveryComponent()
    {
        var name = DownloadPaths.Movie("../CON: test\\evil", 2026);
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('\\', name);
        Assert.DoesNotContain(':', name);
        Assert.False(name.StartsWith('.'));
        Assert.NotEqual("CON.mp4", DownloadPaths.Movie("CON", null));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownloadPaths.Episode("x", "v", -1, 1, "e"));
        Assert.Equal(Path.Combine("x (v)", "Сезон 00", "S00E01 e.mp4"), DownloadPaths.Episode("x", "v", 0, 1, "e"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DownloadPaths.Episode("x", "v", 1, 0, "e"));
    }

    [Fact]
    public void DestinationMustStayInsideLibrary()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "library"));
        Assert.Equal(Path.Combine(root, "Сезон 01", "S01E01.mp4"), DownloadPaths.Resolve(root, Path.Combine("Сезон 01", "S01E01.mp4")));
        Assert.Throws<ArgumentException>(() => DownloadPaths.Resolve(root, Path.Combine("..", "outside.mp4")));
        Assert.Throws<ArgumentException>(() => DownloadPaths.Resolve(root, Path.GetFullPath(Path.Combine(Path.GetTempPath(), "outside.mp4"))));
    }
}
