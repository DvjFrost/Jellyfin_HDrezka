using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class EpisodeScheduleParserTests
{
    [Fact]
    public void PrefersRussianThenOriginalAndOnlyNamesAvailableEpisodes()
    {
        const string html = """
        <div class='b-post__schedule_block'>
          <div class='b-post__schedule_block_title'><div class='title'>2 сезон</div></div>
          <table>
            <tr><td class='td-1'>1 серия</td><td class='td-2'><b>Пилот</b><span>Pilot</span></td><td class='td-4'>01.01.2024</td></tr>
            <tr><td class='td-1'>2 серия</td><td class='td-2'><b></b><span>Original title</span></td></tr>
            <tr><td class='td-1'>3 серия</td><td class='td-2'><b>Ещё не вышла</b></td></tr>
          </table>
        </div>
        """;
        var names = EpisodeScheduleParser.Parse(html);
        var episodes = new[] { new Episode("2", "1", "Серия 1"), new Episode("2", "2", "Серия 2"), new Episode("1", "1", "Серия 1") };
        var result = EpisodeScheduleParser.Apply(episodes, names);
        Assert.Equal(new[] { "Пилот", "Original title", "Серия 1" }, result.Select(item => item.Title));
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void SkipsMalformedOrMissingNumbersAndNames()
    {
        const string html = "<div class='b-post__schedule_block'><div class='b-post__schedule_block_title'><div class='title'>Нет сезона</div></div><tr><td class='td-1'>1 серия</td><td class='td-2'><b>Название</b></td></tr></div>";
        Assert.Empty(EpisodeScheduleParser.Parse(html));
    }

    [Fact]
    public void ReadsSeasonAndEpisodeFromRealScheduleRowAndRejectsMismatchedSeason()
    {
        const string html = "<div class='b-post__schedule_block'><div class='b-post__schedule_block_title'><div class='title'>Доктор Хаус — даты выхода серий 8 сезона</div></div><table><tr><td class='td-1'>8 сезон 22 серия</td><td class='td-2'><b>Все умирают</b><span>Everybody Dies</span></td></tr><tr><td class='td-1'>7 сезон 23 серия</td><td class='td-2'><b>Другая серия</b></td></tr></table></div>";
        var names = EpisodeScheduleParser.Parse(html);
        Assert.Equal("Все умирают", names[(8, 22)]);
        Assert.Single(names);
    }
}
