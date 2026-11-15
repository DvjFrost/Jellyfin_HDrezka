using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record DownloadDestination(string Id, string Name, string Path, bool Series);

public static class DownloadLibraries
{
    public static IReadOnlyList<DownloadDestination> List(IEnumerable<VirtualFolderInfo> folders, string pluginRoot)
    {
        var results = new List<DownloadDestination>();
        var excluded = Path.GetFullPath(pluginRoot);
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder.Name)) continue;
            if (folder.CollectionType is not (CollectionTypeOptions.movies or CollectionTypeOptions.tvshows)) continue;
            foreach (var location in folder.Locations ?? [])
            {
                if (string.IsNullOrWhiteSpace(location) || !Path.IsPathFullyQualified(location)) continue;
                var path = Path.GetFullPath(location);
                if (ContainsPath(excluded, path) || ContainsPath(path, excluded)) continue;
                var normalized = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
                var identifier = folder.CollectionType + "\0" + folder.Name + "\0" + normalized;
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identifier)));
                results.Add(new DownloadDestination(fingerprint, folder.Name, path, folder.CollectionType == CollectionTypeOptions.tvshows));
            }
        }
        return results;
    }

    public static string? Resolve(IEnumerable<VirtualFolderInfo> folders, string pluginRoot, string id, bool series)
        => List(folders, pluginRoot).FirstOrDefault(item => item.Series == series && item.Id.Equals(id, StringComparison.Ordinal))?.Path;

    private static bool ContainsPath(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
