using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<LibraryMediaProvider>();
        services.AddSingleton<DownloadService>();
        services.AddHostedService(sp => sp.GetRequiredService<DownloadService>());
        services.AddHostedService<NfoSaverMigrationService>();
        services.AddHostedService<MirrorSessionRefreshService>();
        services.AddSingleton<SeriesMigration>();
        services.AddSingleton<SitemapDetailCoordinator>();
        services.AddScoped<MultipartResumeFilter>();
        services.AddScoped<SitemapDetailFilter>();
        services.AddScoped<PluginPackageMetadataFilter>();
        services.AddScoped<DashboardConfigurationFilter>();
        services.AddScoped<SeriesIdentifyFilter>();
        services.AddScoped<ResumePosterFallbackFilter>();
        services.Configure<MvcOptions>(options => options.Filters.AddService<ResumePosterFallbackFilter>());
        services.Configure<MvcOptions>(options => options.Filters.AddService<SeriesIdentifyFilter>());
        services.Configure<MvcOptions>(options => options.Filters.AddService<DashboardConfigurationFilter>());
        services.Configure<MvcOptions>(options => options.Filters.AddService<PluginPackageMetadataFilter>());
        services.Configure<MvcOptions>(options => options.Filters.AddService<SitemapDetailFilter>());
        services.Configure<MvcOptions>(options => options.Filters.AddService<MultipartResumeFilter>());
        var persistenceDescriptor = services.LastOrDefault(x => x.ServiceType == typeof(IItemPersistenceService));
        if (persistenceDescriptor is not null)
        {
            services.Remove(persistenceDescriptor);
            services.AddSingleton<IItemPersistenceService>(sp =>
            {
                var inner = (IItemPersistenceService)(persistenceDescriptor.ImplementationInstance ?? persistenceDescriptor.ImplementationFactory?.Invoke(sp)
                    ?? ActivatorUtilities.CreateInstance(sp, persistenceDescriptor.ImplementationType!));
                return new LibraryBatchPersistence(inner, sp.GetRequiredService<IApplicationPaths>(), sp.GetRequiredService<ILogger<LibraryBatchPersistence>>());
            });
        }
        var descriptor = services.LastOrDefault(x => x.ServiceType == typeof(IMediaSourceManager));
        if (descriptor is null) return;
        services.Remove(descriptor);
        services.AddSingleton<IMediaSourceManager>(sp =>
        {
            var inner = (IMediaSourceManager)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(sp)
                ?? ActivatorUtilities.CreateInstance(sp, descriptor.ImplementationType!));
            var proxy = System.Reflection.DispatchProxy.Create<IMediaSourceManager, LibrarySourceProxy>();
            var wrapper = (LibrarySourceProxy)(object)proxy;
            wrapper.Inner = inner;
            wrapper.Provider = sp.GetRequiredService<LibraryMediaProvider>();
            wrapper.Library = sp.GetRequiredService<ILibraryManager>();
            wrapper.Http = sp.GetService<IHttpContextAccessor>();
            return proxy;
        });
    }
}
