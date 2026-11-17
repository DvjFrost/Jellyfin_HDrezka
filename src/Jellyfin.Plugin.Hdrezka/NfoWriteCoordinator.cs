namespace Jellyfin.Plugin.Hdrezka;

public static class NfoWriteCoordinator
{
    public static Task SaveAsync(string path, Func<Task> write, CancellationToken token)
        => MetadataFileLock.RunAsync(path, write, token);
}
