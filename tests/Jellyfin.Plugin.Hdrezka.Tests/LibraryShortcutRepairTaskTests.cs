using Jellyfin.Plugin.Hdrezka;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Model.Tasks;
using Xunit;

public class LibraryShortcutRepairTaskTests
{
    [Fact]
    public void RunsLocallyAtStartup()
    {
        var task = new LibraryShortcutRepairTask(null!, null!, null!);
        Assert.Contains(task.GetDefaultTriggers(), x => x.Type == TaskTriggerInfoType.StartupTrigger);
        Assert.Contains(task.GetDefaultTriggers(), x => x.Type == TaskTriggerInfoType.IntervalTrigger && x.IntervalTicks == TimeSpan.FromDays(1).Ticks);
    }

    [Fact]
    public void SelectsOnlyBrokenGeneratedStreams()
    {
        Assert.True(LibraryShortcutRepairTask.IsCandidate(new Movie { Path = "HDrezka-123 - voice-3.strm", IsShortcut = false }));
        Assert.False(LibraryShortcutRepairTask.IsCandidate(new Movie { Path = "HDrezka-123 - voice-3.strm", IsShortcut = true }));
        Assert.False(LibraryShortcutRepairTask.IsCandidate(new Movie { Path = "Other.strm", IsShortcut = false }));
    }
}
