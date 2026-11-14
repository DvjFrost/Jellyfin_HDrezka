using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class SingleVoiceDetailsTests
{
    [Fact]
    public void ShowsVoiceAndVerifiedQualityWithoutLosingDescription()
    {
        Assert.Equal("Озвучка: Многоголосый закадровый\n\nКачество: 720p\n\nОписание", SingleVoiceDetails.Format("Многоголосый закадровый", "Многоголосый закадровый — 720p", "Описание"));
        Assert.Equal("Озвучка: Дубляж\n\nКачество: не удалось определить", SingleVoiceDetails.Format("Дубляж", null, ""));
    }
}
