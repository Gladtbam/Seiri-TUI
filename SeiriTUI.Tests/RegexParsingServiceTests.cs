using FluentAssertions;
using SeiriTUI.Models;
using SeiriTUI.Services;

namespace SeiriTUI.Tests;

/// <summary>
/// 正则解析模块测试 (RegexParsingService)
/// </summary>
public class RegexParsingServiceTests
{
    private readonly RegexParsingService _parser = new();

    // ==================== 发布组命名兼容性测试 ====================

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
        item.ReleaseGroup.Should().Be("VCB-Studio");
        item.Episode.Should().Be(1);
        item.Resolution.Should().Be("1080p");
        item.VideoCodec.Should().Be("x265");
        item.BitDepth.Should().Be("10bit");
        item.AudioCodec.Should().Be("AAC");
        item.AudioChannel.Should().Be("2.0");
        item.Language.Should().Be("zh-Hans");
    }

    [Fact]
    public void Parse_ShouldHandleStandardSceneRelease()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "The.Last.Of.Us.S01E03.1080p.WEB-DL.x265.mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        item.Season.Should().Be(1);
        item.Episode.Should().Be(3);
        item.Resolution.Should().Be("1080p");
        item.Quality.Should().Be("WEBDL");
        item.VideoCodec.Should().Be("x265");
    }

    [Theory]
    [InlineData("ShowName.S01E01.1080p.WebRip-SceneGroup.mkv", "SceneGroup")]
    [InlineData("Anime - 02 -[SubsPlease].mkv", "SubsPlease")]
    [InlineData("Some_Movie_4K[Release_Group].mp4", "Release_Group")]
    public void Parse_ShouldExtractReleaseGroup_WhenAtTheEnd(string fileName, string expectedGroup)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        item.ReleaseGroup.Should().Be(expectedGroup);
    }

    /// <summary>
    /// 前置组名提取测试
    /// </summary>
    [Theory]
    [InlineData("[VCB-Studio] Anime - 01.mkv", "VCB-Studio")]
    [InlineData("[Beatrice-Raws] Show Title - 05 [BDRip 1920x1080 HEVC].mkv", "Beatrice-Raws")]
    [InlineData("[Snow-Raws] Test 第12話 (BD).mkv", "Snow-Raws")]
    [InlineData("[Nekomoe kissaten] Kusuriya no Hitorigoto [01].mkv", "Nekomoe kissaten")]
    public void Parse_ShouldExtractReleaseGroup_WhenAtTheBeginning(string fileName, string expectedGroup)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        item.ReleaseGroup.Should().Be(expectedGroup);
    }

    [Fact]
    public void Parse_ShouldRestoreSpacesFromDots_WhenSceneFormat()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "The.Last.Of.Us.S01E03.1080p.WEB-DL.x265.mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        item.ParsedShowName.Should().Be("The Last Of Us");
        item.Season.Should().Be(1);
        item.Episode.Should().Be(3);
    }

    [Fact]
    public void Parse_ShouldSupportH264WithDot_And_AudioChannel_WithoutSpace()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "Chitose.Is.in.the.Ramune.Bottle.S01E12.With.a.Chance.of.Dreams.1080p.CR.WEB-DL.JPN.AAC2.0.H.264.MSubs-ToonsHub.mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        item.ParsedShowName.Should().Be("Chitose Is in the Ramune Bottle");
        item.Season.Should().Be(1);
        item.Episode.Should().Be(12);
        item.Resolution.Should().Be("1080p");
        item.Quality.Should().Be("WEBDL");
        item.AudioCodec.Should().Be("AAC");
        item.AudioChannel.Should().Be("2.0");
        item.VideoCodec.Should().Be("x264");
        item.ReleaseGroup.Should().Be("ToonsHub");
    }

    // ==================== 复杂集数提取测试 ====================

    [Fact]
    public void Parse_ShouldExtractEpisodesWithChineseJapaneseFormats()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "[Snow-Raws] ハイスクールDxD 第12話 (BD 1920x1080 HEVC-YUV420P10 FLAC).mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        item.Episode.Should().Be(12);
        item.ReleaseGroup.Should().Be("Snow-Raws");
        item.Resolution.Should().Be("1080p");
        item.Quality.Should().Be("BD");
        item.VideoCodec.Should().Be("x265");
        item.BitDepth.Should().Be("10bit");
        item.AudioCodec.Should().Be("FLAC");
    }

    [Fact]
    public void Parse_ShouldExtractAudioChannelWithoutConfusion()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "You.and.I.Are.Polar.Opposites.S01E11.Class.Trip.Part.2.1080p.CR.WEB-DL.DUAL.AAC2.0.H.264-VARYG.mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        item.AudioChannel.Should().Be("2.0");
        item.Resolution.Should().Be("1080p");
        item.VideoCodec.Should().Be("x264");
        item.AudioCodec.Should().Be("AAC");
        item.ReleaseGroup.Should().Be("VARYG");
    }

    [Fact]
    public void Parse_ShouldExtractSpecialSeasonForOVAs()
    {
        var item1 = new MediaFileItem { OriginalFileName = "[LoliHouse] OAD 01.mkv", Extension = ".mkv" };
        var item2 = new MediaFileItem { OriginalFileName = "Super Anime [OVA 3] 1080p.mp4", Extension = ".mp4" };
        var item3 = new MediaFileItem { OriginalFileName = "Special - 02.mkv", Extension = ".mkv" };

        _parser.Parse(item1);
        _parser.Parse(item2);
        _parser.Parse(item3);

        item1.Season.Should().Be(0);
        item1.Episode.Should().Be(1);

        item2.Season.Should().Be(0);
        item2.Episode.Should().Be(3);

        item3.Season.Should().Be(0);
        item3.Episode.Should().Be(2);
    }

    /// <summary>
    /// 多种集数格式兼容测试：[13], - 08v2, S02E01, EP05 等
    /// </summary>
    [Theory]
    [InlineData("[Group] Show [13].mkv", 13)]
    [InlineData("Show - 08v2 [1080p].mkv", 8)]
    [InlineData("Anime S02E01.mkv", 1)]
    [InlineData("Show EP05 [720p].mkv", 5)]
    public void Parse_ShouldExtractEpisode_FromVariousFormats(string fileName, int expectedEp)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        item.Episode.Should().Be(expectedEp);
    }

    // ==================== 语言标签转化测试 ====================

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
        item.Language.Should().Be(expectedLang);
    }

    /// <summary>
    /// CHS -> zh-Hans, GB -> zh-Hans, BIG5/CHT -> zh-Hant 映射测试
    /// </summary>
    [Theory]
    [InlineData("Show.01.CHS.srt", "zh-Hans")]
    [InlineData("Show.01.GB.srt", "zh-Hans")]
    [InlineData("Show.01.BIG5.srt", "zh-Hant")]
    [InlineData("Show.01.CHT.srt", "zh-Hant")]
    [InlineData("Show.01.SC.srt", "zh-Hans")]
    [InlineData("Show.01.TC.srt", "zh-Hant")]
    public void Parse_ShouldMapLanguageCodes_ToStandardFormat(string fileName, string expectedLang)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        item.Language.Should().Be(expectedLang);
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
        item.Language.Should().BeNull(); // 必须是 null，不能被误识别为 en 或 sc 等
    }

    // ==================== 媒体介质全参数提取测试 ====================

    [Fact]
    public void Parse_ShouldExtractAllMediaProperties_WhenFullySpecified()
    {
        var item = new MediaFileItem
        {
            OriginalFileName = "[GroupName] AnimeName - 01 [BDRip 1080p HEVC 10bit FLAC 5.1].mkv",
            Extension = ".mkv"
        };

        _parser.Parse(item);

        // 六大属性应当全部被准确提取
        item.Quality.Should().Be("BDRip", "质量 (Quality) 提取失败");
        item.Resolution.Should().Be("1080p", "分辨率 (Resolution) 提取失败");
        item.VideoCodec.Should().Be("x265", "视频编码 (VideoCodec/HEVC) 提取失败");
        item.BitDepth.Should().Be("10bit", "色彩深度 (BitDepth) 提取失败");
        item.AudioCodec.Should().Be("FLAC", "音频编码 (AudioCodec) 提取失败");
        item.AudioChannel.Should().Be("5.1", "音频声道 (AudioChannel) 提取失败");
    }

    [Theory]
    [InlineData("Show - 01 [1080p Ma10p].mkv", "10bit", "x265")]
    [InlineData("Show - 02 [720p Hi10p].mkv", "10bit", "x264")]
    [InlineData("Show - 03 [1080p Main10].mkv", "10bit", "x265")]
    [InlineData("Show - 04 [1080p High10].mkv", "10bit", "x264")]
    [InlineData("Show - 05 [1080p AVC Ma10p].mkv", "10bit", "x264")] // AVC overrides deduced x265
    public void Parse_ShouldExtractBitDepthAndDeduceCodec_FromMa10pHi10p(string fileName, string expectedBitDepth, string expectedCodec)
    {
        var item = new MediaFileItem { OriginalFileName = fileName };
        _parser.Parse(item);
        item.BitDepth.Should().Be(expectedBitDepth);
        item.VideoCodec.Should().Be(expectedCodec);
    }
}
