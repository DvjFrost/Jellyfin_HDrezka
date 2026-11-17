using System.Text.Json;

namespace Jellyfin.Plugin.Hdrezka;

public sealed record MultipartResume(Guid Film, Guid Part);

public static class MultipartResumeStore
{
    private static string UserDirectory(string root, Guid user) => Path.Combine(root, ".hdrezka-resume", user.ToString("N"));
    private static string FileName(string root, Guid user, Guid film) => Path.Combine(UserDirectory(root, user), film.ToString("N") + ".json");

    public static async Task PutAsync(string root, Guid user, Guid film, Guid part, CancellationToken token)
    {
        if (user == Guid.Empty || film == Guid.Empty || part == Guid.Empty) throw new ArgumentException("Invalid identity.");
        Directory.CreateDirectory(UserDirectory(root, user));
        await MovieLibraryWriter.AtomicWriteAsync(FileName(root, user, film), JsonSerializer.Serialize(new MultipartResume(film, part)), token).ConfigureAwait(false);
    }

    public static IReadOnlyList<MultipartResume> List(string root, Guid user)
    {
        var directory = UserDirectory(root, user);
        if (!Directory.Exists(directory)) return [];
        var result = new List<MultipartResume>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<MultipartResume>(File.ReadAllText(file));
                if (item is not null && item.Film != Guid.Empty && item.Part != Guid.Empty && Path.GetFileNameWithoutExtension(file) == item.Film.ToString("N")) result.Add(item);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return result;
    }

    public static void Remove(string root, Guid user, Guid film) => File.Delete(FileName(root, user, film));
}
