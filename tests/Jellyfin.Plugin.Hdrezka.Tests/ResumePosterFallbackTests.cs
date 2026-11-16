using Jellyfin.Plugin.Hdrezka;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using Xunit;

public sealed class ResumePosterFallbackTests
{
    [Fact]
    public void UsesPosterOnlyIfEpisodeHasNoOwnImage()
    {
        var dto = new BaseItemDto { Type = BaseItemKind.Episode, SeriesId = Guid.NewGuid(), SeriesPrimaryImageTag = "poster", ImageTags = [] };
        Assert.True(ResumePosterFallbackFilter.NeedsFallback(dto));
        dto.ImageTags[ImageType.Primary] = "frame";
        Assert.False(ResumePosterFallbackFilter.NeedsFallback(dto));
        dto.ImageTags.Clear();
        dto.ImageTags[ImageType.Thumb] = "thumbnail";
        Assert.False(ResumePosterFallbackFilter.NeedsFallback(dto));
        dto.ImageTags.Clear();
        dto.SeriesPrimaryImageTag = null!;
        Assert.False(ResumePosterFallbackFilter.NeedsFallback(dto));
    }
}
