using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.Hdrezka;

public static class MetadataSaveGuard
{
    public static Task RunAsync(BaseItem item, Func<Task> save)
    {
        var nfo = OwnedNfoPath(item);
        return nfo is null ? save() : MetadataFileLock.RunAsync(nfo, save, CancellationToken.None);
    }

    private static string? OwnedNfoPath(BaseItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path)) return null;
        if (item is Series)
        {
            var dir = item.Path;
            return IsOwnedDirectory(dir, "hdrezka-series.json") ? Path.Combine(dir, "tvshow.nfo") : null;
        }
        if (item is not Video || !string.Equals(Path.GetExtension(item.Path), ".strm", StringComparison.OrdinalIgnoreCase)) return null;
        var parent = Path.GetDirectoryName(item.Path);
        return parent is not null && IsOwnedDirectory(parent, "hdrezka.json") ? Path.Combine(parent, "movie.nfo") : null;
    }

    private static bool IsOwnedDirectory(string directory, string record) =>
        Regex.IsMatch(Path.GetFileName(directory), @"^HDrezka-[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
        && File.Exists(Path.Combine(directory, record));
}

// LibraryManager calls the provider through this interface. Use the same lock
// as our NFO writers so Jellyfin's saver and the plugin cannot write together.
public class MetadataProviderProxy : DispatchProxy
{
    public IProviderManager Inner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (method.Name == nameof(IProviderManager.SaveMetadataAsync) && args?.FirstOrDefault() is BaseItem item)
            return MetadataSaveGuard.RunAsync(item, () => (Task)InvokeInner(method, args)!);
        return InvokeInner(method, args);
    }

    private object? InvokeInner(MethodInfo method, object?[]? args)
    {
        try { return method.Invoke(Inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
}
