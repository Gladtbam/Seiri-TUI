using SeiriTUI.Models;
using SeiriTUI.Services;
using Xunit;

namespace SeiriTUI.Tests;

public class RegexParsingServiceTests
{
    private readonly RegexParsingService _parser = new();

    [Fact]
    public void Parse_ShouldExtractCorrectInformation_WhenValidShowName()
    {
        // Arrange
        var item = new MediaFileItem
        {
            OriginalFileName = "[VCB-Studio] Oshi no Ko [01][1080p][HEVC 10bit][AAC 2.0][CHS].mkv",
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
        Assert.Equal("AAC", item.AudioCodec);
        Assert.Equal("2.0", item.AudioChannel);
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
        Assert.Equal("WEBDL", item.Quality);
        Assert.Equal("x265", item.VideoCodec);
    }

    [Theory]
    [InlineData("ShowName.S01E01.1080p.WebRip-SceneGroup.mkv", "SceneGroup")]
    [InlineData("Anime - 02 -[SubsPlease].mkv", "SubsPlease")]
    [InlineData("Some_Movie_4K[Release_Group].mp4", "Release_Group")]
    public void Parse_ShouldExtractReleaseGroup_WhenAtTheEnd(string fileName, string expectedGroup)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        Assert.Equal(expectedGroup, item.ReleaseGroup);
    }

    [Fact]
    public void Parse_ShouldRestoreSpacesFromDots_WhenSceneFormat()
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
        Assert.Equal("The Last Of Us", item.ParsedShowName);
        Assert.Equal(1, item.Season);
        Assert.Equal(3, item.Episode);
    }

    [Fact]
    public void Parse_ShouldSupportH264WithDot_And_AudioChannel_WithoutSpace()
    {
        // Arrange
        var item = new MediaFileItem
        {
            OriginalFileName = "Chitose.Is.in.the.Ramune.Bottle.S01E12.With.a.Chance.of.Dreams.1080p.CR.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            Extension = ".mkv"
        };

        // Act
        _parser.Parse(item);

        // Assert
        Assert.Equal("Chitose Is in the Ramune Bottle", item.ParsedShowName);
        Assert.Equal(1, item.Season);
        Assert.Equal(12, item.Episode);
        Assert.Equal("1080p", item.Resolution);
        Assert.Equal("WEBDL", item.Quality);
        Assert.Equal("AAC", item.AudioCodec);
        Assert.Equal("2.0", item.AudioChannel);
        Assert.Equal("x264", item.VideoCodec);
        Assert.Equal("ToonsHub", item.ReleaseGroup);
    }

    [Theory]
    [InlineData("[VCB-Studio] Anime S01E01 [CHS].ass", "zh-Hans")]
    [InlineData("ShowName.S02E05.CHT.ass", "zh-Hant")]
    [InlineData("Movie_720p_en_forced.srt", "eng")]
    [InlineData("Something - 01 - jp.srt", "jpn")]
    [InlineData("Dual [jp_sc].ass", "zh-Hans")]
    [InlineData("Anime - 02 (chs&jp).ass", "zh-Hans")]
    public void Parse_ShouldExtractCorrectLanguage_WithBoundaries(string fileName, string expectedLang)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        Assert.Equal(expectedLang, item.Language);
    }

    [Theory]
    [InlineData("[Nekomoe kissaten] Kusuriya no Hitorigoto 2nd Season [25].ass")] // kissaten 包含了 en, season 包含了 sc 等字眼，应被忽略
    [InlineData("Tencent Video.mp4")] // Tencent包含en
    [InlineData("Escape Room.mkv")] // Escape包含sc
    [InlineData("The Great Pretender.mkv")] // Pretender包含en
    public void Parse_ShouldIgnoreLanguage_WhenEmbeddedInsideNormalWords(string fileName)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        Assert.Null(item.Language); // 必须是 null，不能被误识别为 en 或 sc 等
    }
}
