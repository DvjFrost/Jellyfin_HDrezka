using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbEnrichmentRulesTests
{
    [Fact]
    public void EnrichesConfirmedMatchesOnceAndRespectsLocksAndChangedIdentifiers()
    {
        Assert.True(TmdbEnrichmentRules.ShouldFetch(42, "42", null, false));
        Assert.False(TmdbEnrichmentRules.ShouldFetch(42, "42", "42", false));
        Assert.False(TmdbEnrichmentRules.ShouldFetch(42, "42", null, true));
        Assert.False(TmdbEnrichmentRules.ShouldFetch(42, "43", null, false));
        Assert.False(TmdbEnrichmentRules.ShouldFetch(null, "42", null, false));
        Assert.True(TmdbEnrichmentRules.ShouldFetch(42, "42", "41", false));
    }
}
