using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbMatchStoreTests
{
    [Fact]
    public async Task ReadsWhileWriterHasFileOpen()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-tmdb-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "matches.jsonl");
            var store = new TmdbMatchStore(path);
            await store.AppendAsync(new("1", TmdbMatchStatus.NoResults, null, null, null), CancellationToken.None);
            await using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            Assert.Single(await store.LoadAsync(CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ResumesResultsAndKeepsUncertainSeparateFromErrors()
    {
        var root = Path.Combine(Path.GetTempPath(), "hdrezka-tmdb-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new TmdbMatchStore(Path.Combine(root, "matches.jsonl"));
            await store.AppendAsync(new("1", TmdbMatchStatus.Matched, 50, "Name", "/poster.jpg"), CancellationToken.None);
            await store.AppendAsync(new("2", TmdbMatchStatus.Uncertain, null, null, null), CancellationToken.None);
            var read = await store.LoadAsync(CancellationToken.None);
            Assert.Equal(2, read.Count);
            Assert.Equal(TmdbMatchStatus.Matched, read["1"].Status);
            Assert.Equal(TmdbMatchStatus.Uncertain, read["2"].Status);
            await File.AppendAllTextAsync(Path.Combine(root, "matches.jsonl"), "{broken");
            await store.AppendAsync(new("3", TmdbMatchStatus.NoResults, null, null, null), CancellationToken.None);
            Assert.Equal(3, (await store.LoadAsync(CancellationToken.None)).Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
