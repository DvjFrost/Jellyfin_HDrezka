using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class HdrezkaConfiguration : BasePluginConfiguration
{
    public string CdnHost { get; set; } = "";
    public string MirrorUrl { get; set; } = "https://rezka-tv.org/";
    public string LibraryRoot { get; set; } = "";
    public string TmdbReadAccessToken { get; set; } = "";
    public int PreferredQuality { get; set; }
}

public sealed class Plugin : BasePlugin<HdrezkaConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) { Instance = this; MirrorSessionRegistry.Configure(paths.DataPath); }
    public static Plugin? Instance { get; private set; }
    public override string Name => "HDrezka";
    public override string Description => "Каталог фильмов и сериалов HDrezka с данными TMDB и бесплатными озвучками. Разработчики: Frost & Codex.";
    public override Guid Id => new("e92a7808-6dc0-45a0-ae64-6d19caa8e93a");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new() { Name = Name, EmbeddedResourcePath = "Jellyfin.Plugin.Hdrezka.Configuration.config.html" },
        new() { Name = "HDrezkaDownloads", DisplayName = "Загрузки HDrezka", EmbeddedResourcePath = "Jellyfin.Plugin.Hdrezka.Configuration.downloads.html", EnableInMainMenu = true, MenuIcon = "download" }
    ];
}

