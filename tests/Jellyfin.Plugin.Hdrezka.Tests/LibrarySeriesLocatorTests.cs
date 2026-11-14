using System.Text.Json;
using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class LibrarySeriesLocatorTests
{
    [Fact]
    public async Task FindsOnlyValidatedOwnedSeries()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-series-record-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dir = Path.Combine(root, "series", "HDrezka-123");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "hdrezka-series.json"), JsonSerializer.Serialize(new LibrarySeriesRecord("123", "/series/a/123-show.html")));
            Assert.Equal("123", LibrarySeriesLocator.Load(root, dir)?.Id);
            Assert.Null(LibrarySeriesLocator.Load(root, Path.GetTempPath()));
            await File.WriteAllTextAsync(Path.Combine(dir, "hdrezka-series.json"), JsonSerializer.Serialize(new LibrarySeriesRecord("123", "/series/a/999-other.html")));
            Assert.Null(LibrarySeriesLocator.Load(root, dir));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
