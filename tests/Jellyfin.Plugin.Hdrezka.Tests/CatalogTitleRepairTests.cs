using Jellyfin.Plugin.Hdrezka;
using Xunit;

public class CatalogTitleRepairTests
{
    [Theory]
    [InlineData("7 dney do svadby", "/films/comedy/80248-7-dney-do-svadby-2007.html", true)]
    [InlineData("HDrezka-80248", "/films/comedy/80248-7-dney-do-svadby-2007.html", true)]
    [InlineData("Моё название", "/films/comedy/80248-7-dney-do-svadby-2007.html", false)]
    [InlineData("7 дней до свадьбы", "/films/comedy/80248-7-dney-do-svadby-2007.html", false)]
    public void RecognizesOnlyGeneratedSitePlaceholders(string title, string path, bool expected)
        => Assert.Equal(expected, CatalogTitleRepair.IsSitePlaceholder(title, path));
    [Theory]
    [InlineData("anna karenina", "anna karenina", "Анна Каренина", false, true)]
    [InlineData("Моё название", "anna karenina", "Анна Каренина", false, false)]
    [InlineData("anna karenina", "anna karenina", "Анна Каренина", true, false)]
    [InlineData("anna karenina", "anna karenina", "", false, false)]
    [InlineData("anna karenina", "anna karenina", "anna karenina", false, false)]
    public void OnlyReplacesUnlockedOriginalPlaceholder(string current, string placeholder, string title, bool locked, bool expected)
        => Assert.Equal(expected, CatalogTitleRepair.ShouldReplace(current, placeholder, title, locked));
}
