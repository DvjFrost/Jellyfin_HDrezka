using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MovieVersionPathsTests
{
    [Fact]
    public void IncludesOnlyExistingGeneratedVersionsAndPreservesExistingLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-movie-versions-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var voice = new FreeVoice("2", "123", "Second", "0", "0", "0");
            var first = new FreeVoice("1", "123", "First", "0", "0", "0");
            var generated = Path.Combine(root, "HDrezka-123 - voice-2.strm");
            File.WriteAllText(generated, "placeholder");
            var existing = Path.Combine(root, "old.strm");
            File.WriteAllText(existing, "placeholder");
            var paths = MovieVersionPaths.Collect(root, "123", [first, voice], [existing]);
            Assert.Contains(existing, paths);
            Assert.Contains(generated, paths);
            Assert.Equal(2, paths.Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
