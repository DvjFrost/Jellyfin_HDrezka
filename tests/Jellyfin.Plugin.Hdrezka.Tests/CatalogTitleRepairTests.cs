using Jellyfin.Plugin.Hdrezka;
using Xunit;

public class CatalogTitleRepairTests
{
    [Theory]
    [InlineData("anna karenina", "anna karenina", "Анна Каренина", false, true)]
    [InlineData("Моё название", "anna karenina", "Анна Каренина", false, false)]
    [InlineData("anna karenina", "anna karenina", "Анна Каренина", true, false)]
    [InlineData("anna karenina", "anna karenina", "", false, false)]
    [InlineData("anna karenina", "anna karenina", "anna karenina", false, false)]
    public void OnlyReplacesUnlockedOriginalPlaceholder(string current, string placeholder, string title, bool locked, bool expected)
        => Assert.Equal(expected, CatalogTitleRepair.ShouldReplace(current, placeholder, title, locked));
}
