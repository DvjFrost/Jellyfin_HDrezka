using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Hdrezka;

public sealed class SeriesMigrationFiles
{
    private readonly List<(string From, string To, bool Directory)> _moves = [];
    public string BackupDirectory { get; }

    private SeriesMigrationFiles(string backupDirectory) => BackupDirectory = backupDirectory;

    public static SeriesMigrationFiles Begin(string root, string itemDirectory, string stagedDirectory, string id)
    {
        if (!Regex.IsMatch(id, "^[1-9][0-9]{0,15}$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid ID", nameof(id));
        if (!File.Exists(Path.Combine(stagedDirectory, "tvshow.nfo")) || !File.Exists(Path.Combine(stagedDirectory, "hdrezka-series.json")))
            throw new InvalidDataException("Staged series is incomplete.");
        var backup = Path.Combine(root, ".hdrezka-migrations", id + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var transaction = new SeriesMigrationFiles(backup);
        try
        {
            foreach (var file in Directory.EnumerateFiles(itemDirectory))
            {
                var name = Path.GetFileName(file);
                if (name is "movie.nfo" or "hdrezka.json" or "hdrezka-catalog.json" ||
                    Regex.IsMatch(name, "^HDrezka-" + id + @"(?: - voice-[0-9]{1,16})?\.strm$", RegexOptions.CultureInvariant))
                    transaction.Move(file, Path.Combine(backup, name), false);
            }
            foreach (var path in Directory.EnumerateFileSystemEntries(stagedDirectory))
                transaction.Move(path, Path.Combine(itemDirectory, Path.GetFileName(path)), Directory.Exists(path));
            return transaction;
        }
        catch { transaction.Rollback(); throw; }
    }

    private void Move(string from, string to, bool directory)
    {
        if (directory) Directory.Move(from, to); else File.Move(from, to);
        _moves.Add((from, to, directory));
    }

    public void Rollback()
    {
        for (var i = _moves.Count - 1; i >= 0; i--)
        {
            var (from, to, directory) = _moves[i];
            if (directory) Directory.Move(to, from); else File.Move(to, from);
        }
        _moves.Clear();
    }
}
