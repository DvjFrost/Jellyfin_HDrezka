using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Controller.Channels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public sealed class CleanServerRegistrationTests
{
    [Fact]
    public void DoesNotRegisterLegacyPlaylistChannel()
    {
        var services = new ServiceCollection();
        new ServiceRegistrator().RegisterServices(services, null!);
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IChannel));
    }
}
