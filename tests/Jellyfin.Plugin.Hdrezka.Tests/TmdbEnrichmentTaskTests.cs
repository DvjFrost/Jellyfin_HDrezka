using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbEnrichmentTaskTests
{
    [Fact]
    public void EnrichmentWaitsForCompleteSitemapCatalog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hdrezka-ready-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            Assert.False(TmdbEnrichmentTask.IsCatalogReady(directory));
            File.WriteAllText(Path.Combine(directory, "sitemap.json"), "[]");
            Assert.False(TmdbEnrichmentTask.IsCatalogReady(directory));
            File.WriteAllText(Path.Combine(directory, "catalog-ready"), "ready");
            Assert.True(TmdbEnrichmentTask.IsCatalogReady(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
