using Jellyfin.Plugin.Hdrezka;
using Xunit;

public sealed class EpisodeParserTests
{
    [Fact]
    public void ReadsSeasonsAndEpisodesInOrder()
    {
        const string html = "<ul class='b-simple_episodes__list' id='simple-episodes-list-1'><li class='b-simple_episode__item' data-episode_id='1'>First</li><li class='b-simple_episode__item' data-episode_id='2'>Second</li></ul><ul class='b-simple_episodes__list' id='simple-episodes-list-2'><li class='b-simple_episode__item' data-episode_id='3'>Third</li></ul>";
        var episodes = EpisodeParser.Parse(html);
        Assert.Collection(episodes,
            x => { Assert.Equal("1", x.Season); Assert.Equal("1", x.Number); },
            x => { Assert.Equal("1", x.Season); Assert.Equal("2", x.Number); },
            x => { Assert.Equal("2", x.Season); Assert.Equal("3", x.Number); });
    }
    [Fact]
    public void SkipsBadAndDuplicateEpisodeIds()
    {
        const string html = "<ul class='b-simple_episodes__list' id='simple-episodes-list-2'><li class='b-simple_episode__item' data-episode_id='x'>X</li><li class='b-simple_episode__item' data-episode_id='1'>A</li><li class='b-simple_episode__item' data-episode_id='1'>A</li></ul>";
        Assert.Single(EpisodeParser.Parse(html));
    }
}

