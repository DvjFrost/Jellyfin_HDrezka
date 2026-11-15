using System.Xml.Linq;

namespace Jellyfin.Plugin.Hdrezka;

public static class DownloadMetadata
{
    public static async Task WriteAsync(string destination, DownloadJob job, CancellationToken token)
    {
        var nfoPath = Path.ChangeExtension(destination, ".nfo");
        if (File.Exists(nfoPath)) return;
        XDocument document;
        if (job.Season is { } season && job.Episode is { } number)
        {
            var title = string.IsNullOrWhiteSpace(job.EpisodeTitle) ? $"Серия {number}" : job.EpisodeTitle;
            document = new XDocument(new XElement("episodedetails", new XElement("title", title), new XElement("season", season), new XElement("episode", number)));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(job.DisplayTitle)) return;
            document = new XDocument(new XElement("movie", new XElement("title", job.DisplayTitle), job.Year is >= 1888 and <= 9999 ? new XElement("year", job.Year) : null));
        }
        var temporary = nfoPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, document.ToString(), token).ConfigureAwait(false);
            try { File.Move(temporary, nfoPath, false); }
            catch (IOException) when (File.Exists(nfoPath)) { }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
