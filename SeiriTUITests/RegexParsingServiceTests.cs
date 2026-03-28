using SeiriTUI.Models;
using SeiriTUI.Services;
using Xunit;

namespace SeiriTUITests;

public class RegexParsingServiceTests
{
    private readonly RegexParsingService _parser = new();

    [Fact]
    public void Parse_ShouldExtractCorrectInformation_WhenValidShowName()
    {
        // Arrange
        var item = new MediaFileItem
        {
            OriginalFileName = "[VCB-Studio] Oshi no Ko [01][1080p][HEVC 10bit][CHS].mkv",
            Extension = ".mkv"
        };

        // Act
        _parser.Parse(item);

        // Assert
        Assert.Equal("VCB-Studio", item.ReleaseGroup);
        Assert.Equal(1, item.Episode);
        Assert.Equal("1080p", item.Resolution);
        Assert.Equal("x265", item.VideoCodec);
        Assert.Equal("10bit", item.BitDepth);
        Assert.Equal("zh-Hans", item.Language);
    }

    [Fact]
    public void Parse_ShouldHandleStandardSceneRelease()
    {
        // Arrange
        var item = new MediaFileItem
        {
            OriginalFileName = "The.Last.Of.Us.S01E03.1080p.WEB-DL.x265.mkv",
            Extension = ".mkv"
        };

        // Act
        _parser.Parse(item);

        // Assert
        Assert.Equal(1, item.Season);
        Assert.Equal(3, item.Episode);
        Assert.Equal("1080p", item.Resolution);
        Assert.Equal("WEB-DL", item.Quality);
        Assert.Equal("x265", item.VideoCodec);
    }

    [Fact]
    public void Parse_ShouldExtractSubtitleLanguage()
    {
        // Arrange
        var item = new MediaFileItem
        {
            OriginalFileName = "ShowName.S02E05.CHT.ass",
            Extension = ".ass"
        };

        // Act
        _parser.Parse(item);

        // Assert
        Assert.Equal(2, item.Season);
        Assert.Equal(5, item.Episode);
        Assert.Equal("zh-Hant", item.Language);
    }
}
