using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class TmdbSearchTests
{
    [Theory]
    [InlineData("dnevnik gornichnoy", "дневник горничной")]
    [InlineData("moy milyy psih", "мой милый псих")]
    [InlineData("avtobusnaya ostanovka", "автобусная остановка")]
    public void ProvidesCyrillicCandidate(string slug, string expected) => Assert.Equal(expected, TmdbSearch.ToCyrillic(slug));

    [Fact]
    public void UnknownLatinLettersDoNotCrashIndexing() => Assert.Equal("wxq", TmdbSearch.ToCyrillic("wxq"));

    [Fact]
    public void ExactTitleAndYearAreRequiredForAutomaticMatch()
    {
        var candidates = new[] { new TmdbCandidate(1, "Дневник горничной", "The Maid", "2026-01-01", "/poster.jpg"), new TmdbCandidate(2, "Дневник горничной", "Other", "2015-01-01", null) };
        Assert.Equal(1, TmdbSearch.Choose("dnevnik gornichnoy", 2026, candidates)?.Id);
        Assert.Null(TmdbSearch.Choose("dnevnik gornichnoy", 2011, candidates));
        Assert.Null(TmdbSearch.Choose("unrelated", 2026, candidates));
    }
}
