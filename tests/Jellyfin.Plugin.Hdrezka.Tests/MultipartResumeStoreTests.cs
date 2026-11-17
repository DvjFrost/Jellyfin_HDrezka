using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class MultipartResumeStoreTests
{
    [Fact]
    public async Task SavesLatestPartPerUserAndFilm()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-resume-" + Guid.NewGuid().ToString("N"));
        try
        {
            var user = Guid.NewGuid(); var another = Guid.NewGuid(); var film = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
            await MultipartResumeStore.PutAsync(root, user, film, first, CancellationToken.None);
            await MultipartResumeStore.PutAsync(root, user, film, second, CancellationToken.None);
            Assert.Equal(second, Assert.Single(MultipartResumeStore.List(root, user)).Part);
            Assert.Empty(MultipartResumeStore.List(root, another));
            MultipartResumeStore.Remove(root, user, film);
            Assert.Empty(MultipartResumeStore.List(root, user));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
