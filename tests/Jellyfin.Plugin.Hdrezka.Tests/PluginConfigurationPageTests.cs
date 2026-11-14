using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class PluginConfigurationPageTests
{
    [Fact]
    public void ConfigurationPageIsEmbedded()
    {
        using var page = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Hdrezka.Configuration.config.html");
        Assert.NotNull(page);
        using var reader = new StreamReader(page);
        var html = reader.ReadToEnd();
        Assert.Contains("TmdbReadAccessToken", html);
        Assert.Contains("MirrorUrl", html);
        Assert.Contains("updateScheduledTaskTriggers", html);
        Assert.Contains("syncDays", html);
    }
}
