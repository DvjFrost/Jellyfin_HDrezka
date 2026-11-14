using System.Reflection;
using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class LibrarySourceProxyTests
{
    [Fact]
    public void ResolvesLibraryRootOnceInsteadOfEnumeratingFoldersForEachItem()
    {
        var calls = 0;
        var paths = Stub<IApplicationPaths>((method, _) => method.Name == "get_ProgramDataPath" ? Path.GetTempPath() : null);
        var library = Stub<ILibraryManager>((method, _) => { if (method.Name == nameof(ILibraryManager.GetVirtualFolders)) calls++; return null; });
        var provider = new LibraryMediaProvider(paths, library, null!, NullLogger<LibraryMediaProvider>.Instance);
        Assert.Equal(provider.Root, provider.Root);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task EmptyFreeVoicesReturnsUnplayablePlaceholderWithoutNetwork()
    {
        var parent = Path.Combine(Path.GetTempPath(), "hdrezka-proxy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = Path.Combine(parent, "hdrezka-library");
            var entry = new CatalogEntry("Title", new Uri("https://example.test/films/a/123-title.html"), null, "", CatalogKind.Movie);
            await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Title", "", false), [], CancellationToken.None);
            var item = new Movie { Id = Guid.NewGuid(), Path = Path.Combine(root, "HDrezka-123", "HDrezka-123.strm") };
            var paths = Stub<IApplicationPaths>((method, _) => method.Name == "get_ProgramDataPath" ? parent : null);
            var library = Stub<ILibraryManager>((method, _) => method.Name == nameof(ILibraryManager.GetItemById) ? item : null);
            var inner = Stub<IMediaSourceManager>((method, _) => method.Name == nameof(IMediaSourceManager.GetStaticMediaSources)
                ? new MediaSourceInfo[] { new() { Id = item.Id.ToString("N"), Name = "stale paid voice" } } : null);
            var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
            http.HttpContext.Request.Path = "/Items/" + item.Id.ToString("N");
            var wrapper = DispatchProxy.Create<IMediaSourceManager, LibrarySourceProxy>();
            var proxy = (LibrarySourceProxy)(object)wrapper;
            proxy.Inner = inner; proxy.Library = library; proxy.Http = http;
            proxy.Provider = new LibraryMediaProvider(paths, library, null!, NullLogger<LibraryMediaProvider>.Instance);
            var result = wrapper.GetStaticMediaSources(item, true);
            var source = Assert.Single(result);
            Assert.Equal("Нет бесплатных озвучек", source.Name);
            Assert.False(source.SupportsDirectPlay);
            Assert.False(source.SupportsTranscoding);
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }

    [Fact]
    public async Task EmptyFreeVoicesAreHiddenEvenOutsideDetailsRequest()
    {
        var parent = Path.Combine(Path.GetTempPath(), "hdrezka-proxy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var root = Path.Combine(parent, "hdrezka-library");
            var entry = new CatalogEntry("Title", new Uri("https://example.test/films/a/123-title.html"), null, "", CatalogKind.Movie);
            await MovieLibraryWriter.WriteAsync(root, entry, new CatalogDetail("Title", "", false), [], CancellationToken.None);
            var item = new Movie { Id = Guid.NewGuid(), Path = Path.Combine(root, "HDrezka-123", "HDrezka-123.strm") };
            var paths = Stub<IApplicationPaths>((method, _) => method.Name == "get_ProgramDataPath" ? parent : null);
            var library = Stub<ILibraryManager>((method, _) => method.Name == nameof(ILibraryManager.GetItemById) ? item : null);
            var inner = Stub<IMediaSourceManager>((method, _) => method.Name == nameof(IMediaSourceManager.GetStaticMediaSources) ? new MediaSourceInfo[] { new() { Id = item.Id.ToString("N"), Name = "stale paid voice" } } : null);
            var wrapper = DispatchProxy.Create<IMediaSourceManager, LibrarySourceProxy>();
            var proxy = (LibrarySourceProxy)(object)wrapper;
            proxy.Inner = inner; proxy.Library = library; proxy.Provider = new LibraryMediaProvider(paths, library, null!, NullLogger<LibraryMediaProvider>.Instance);
            Assert.Equal("Нет бесплатных озвучек", Assert.Single(wrapper.GetStaticMediaSources(item, true)).Name);
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }

    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        var result = DispatchProxy.Create<T, FakeProxy>();
        ((FakeProxy)(object)result).Handler = invoke;
        return result;
    }

    public class FakeProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
}
