using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SeriesMigrationFilesTests
{
    [Fact]
    public void RollbackRestoresMovieAndStagedSeries()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-migration-" + Guid.NewGuid().ToString("N"));
        try
        {
            var movie = Path.Combine(root, "cartoons", "HDrezka-123");
            var stage = Path.Combine(root, ".hdrezka-staging", "123", "HDrezka-123");
            Directory.CreateDirectory(movie); Directory.CreateDirectory(stage);
            File.WriteAllText(Path.Combine(movie, "movie.nfo"), "old");
            File.WriteAllText(Path.Combine(movie, "HDrezka-123.strm"), "stream");
            File.WriteAllText(Path.Combine(movie, "unrelated.txt"), "keep");
            File.WriteAllText(Path.Combine(stage, "tvshow.nfo"), "new");
            File.WriteAllText(Path.Combine(stage, "hdrezka-series.json"), "series");
            var transition = SeriesMigrationFiles.Begin(root, movie, stage, "123");
            Assert.False(File.Exists(Path.Combine(movie, "movie.nfo")));
            Assert.True(File.Exists(Path.Combine(movie, "tvshow.nfo")));
            transition.Rollback();
            Assert.Equal("old", File.ReadAllText(Path.Combine(movie, "movie.nfo")));
            Assert.Equal("stream", File.ReadAllText(Path.Combine(movie, "HDrezka-123.strm")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(movie, "unrelated.txt")));
            Assert.Equal("new", File.ReadAllText(Path.Combine(stage, "tvshow.nfo")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
