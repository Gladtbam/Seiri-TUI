using FluentAssertions;
using SeiriTUI.Models;
using SeiriTUI.ViewModels;

namespace SeiriTUI.Tests;

/// <summary>
/// 独立字幕匹配逻辑测试
/// </summary>
public class SubtitleMatchingTests
{
    /// <summary>
    /// 绑定关联断言：
    /// 向 ViewModel 注入命名完全不同的视频和字幕列表，
    /// 断言能通过相同的 S/E 准确将字幕的 AssociatedVideoItem 属性指向正确的视频。
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldBindToCorrectVideo_BySeasonAndEpisode()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalSeason = 1;

        // 注入命名完全不同的视频文件
        var video1 = new MediaFileItem
        {
            OriginalFileName = "[VCB-Studio] Anime Title [01][1080p][x265 10bit].mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            ParsedShowName = "Anime Title",
            Season = 1,
            Episode = 1,
            Quality = "BD",
            Resolution = "1080p",
            VideoCodec = "x265",
            BitDepth = "10bit"
        };

        var video2 = new MediaFileItem
        {
            OriginalFileName = "[VCB-Studio] Anime Title [02][1080p][x265 10bit].mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            ParsedShowName = "Anime Title",
            Season = 1,
            Episode = 2,
            Quality = "BD",
            Resolution = "1080p",
            VideoCodec = "x265",
            BitDepth = "10bit"
        };

        // 注入命名完全不同的字幕文件
        var sub1 = new MediaFileItem
        {
            OriginalFileName = "[Sakurato] Totally Different Name [01][CHS].ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            ParsedShowName = "Totally Different Name",
            Season = 1,
            Episode = 1,
            Language = "zh-Hans",
            ReleaseGroup = "Sakurato"
        };

        var sub2 = new MediaFileItem
        {
            OriginalFileName = "[Sakurato] Totally Different Name [02][CHS].ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            ParsedShowName = "Totally Different Name",
            Season = 1,
            Episode = 2,
            Language = "zh-Hans",
            ReleaseGroup = "Sakurato"
        };

        vm.MediaFiles.Add(video1);
        vm.MediaFiles.Add(video2);
        vm.MediaFiles.Add(sub1);
        vm.MediaFiles.Add(sub2);

        // Act: 触发全量重计算
        vm.RecalculateTargetFileName(video1);
        vm.RecalculateTargetFileName(video2);
        vm.RecalculateTargetFileName(sub1);
        vm.RecalculateTargetFileName(sub2);

        // Assert: 字幕应通过相同的 S/E 准确绑定到正确的视频
        sub1.AssociatedVideoItem.Should().BeSameAs(video1,
            "S01E01 的字幕应绑定到 S01E01 的视频");
        sub2.AssociatedVideoItem.Should().BeSameAs(video2,
            "S01E02 的字幕应绑定到 S01E02 的视频");
    }

    /// <summary>
    /// 字幕命名格式断言：
    /// 断言生成的字幕名符合 [VideoName].[Group].[Lang].ext 规范，
    /// 且在无 Group 时不会出现连续点号 `..`。
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldGenerateCorrectName_WithGroupAndLanguage()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalShowName = "ShowName";

        var video = new MediaFileItem
        {
            OriginalFileName = "video_ep01.mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            Season = 1,
            Episode = 1,
            Quality = "BD",
            Resolution = "1080p"
        };

        // 有字幕组和语言的字幕
        var subWithGroup = new MediaFileItem
        {
            OriginalFileName = "random_sub_01.ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1,
            ReleaseGroup = "SubsPlease",
            Language = "zh-CN"
        };

        vm.MediaFiles.Add(video);
        vm.MediaFiles.Add(subWithGroup);

        // Act
        vm.RecalculateTargetFileName(video);
        vm.RecalculateTargetFileName(subWithGroup);

        // Assert: 格式应为 [VideoOriginalBaseName].[Group].[Lang].ext
        // 视频原始名：video_ep01.mkv -> 基础名为 video_ep01
        subWithGroup.TargetFileName.Should().EndWith(".SubsPlease.zh-CN.ass",
            "字幕名应包含 [Group].[Lang].ext 后缀");
        subWithGroup.TargetFileName.Should().Contain("video_ep01",
            "字幕名应基于匹配的视频原始名");
    }

    /// <summary>
    /// 字幕命名格式断言（无 Group 时）：
    /// 不应出现连续点号 `..`
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldNotHaveDoubleDots_WhenNoGroup()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalShowName = "ShowName";

        var video = new MediaFileItem
        {
            OriginalFileName = "video.mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            Season = 1,
            Episode = 1,
            Resolution = "1080p"
        };

        // 无字幕组，仅有语言
        var subNoGroup = new MediaFileItem
        {
            OriginalFileName = "sub.srt",
            Extension = ".srt",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1,
            Language = "zh-CN"
            // ReleaseGroup is null - no group
        };

        vm.MediaFiles.Add(video);
        vm.MediaFiles.Add(subNoGroup);

        // Act
        vm.RecalculateTargetFileName(video);
        vm.RecalculateTargetFileName(subNoGroup);

        // Assert: 不应出现连续 ".."
        subNoGroup.TargetFileName.Should().NotContain("..",
            "缺少 Group 时不应产生连续的点号");
        subNoGroup.TargetFileName.Should().EndWith(".zh-CN.srt",
            "应直接跟语言后缀");
    }

    /// <summary>
    /// 默认字幕语言断言：
    /// 断言在未输入"语言"参数时，字幕组名称 [xxx] 后面的 `.` 不会缺失，
    /// 即生成的字幕名末尾不会出现连续的 `.`。
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldHandleNoLanguage_WithGroup()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalShowName = "ShowName";
        vm.DefaultSubtitleLanguage = ""; // 未设定默认语言

        var video = new MediaFileItem
        {
            OriginalFileName = "video.mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            Season = 1,
            Episode = 1,
            Resolution = "1080p"
        };

        var subGroupNoLang = new MediaFileItem
        {
            OriginalFileName = "sub.ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1,
            ReleaseGroup = "GroupName"
            // Language is null, DefaultSubtitleLanguage is empty
        };

        vm.MediaFiles.Add(video);
        vm.MediaFiles.Add(subGroupNoLang);

        // Act
        vm.RecalculateTargetFileName(video);
        vm.RecalculateTargetFileName(subGroupNoLang);

        // Assert: 有 Group 无 Language 时，末尾不应出现 ".." 或 ".ass" 前面的多余点
        subGroupNoLang.TargetFileName.Should().NotContain("..",
            "有 Group 无 Language 时不应产生连续点号");
        subGroupNoLang.TargetFileName.Should().EndWith(".GroupName.ass",
            "仅 Group 后缀，直接接扩展名");
    }

    /// <summary>
    /// 默认字幕语言断言（补充）：
    /// 当既无 Group 也无 Language 时，不应出现多余的点号
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldHandleNoGroupNoLanguage()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalShowName = "ShowName";
        vm.DefaultSubtitleLanguage = "";

        var video = new MediaFileItem
        {
            OriginalFileName = "video.mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            Season = 1,
            Episode = 1,
            Resolution = "1080p"
        };

        var subBare = new MediaFileItem
        {
            OriginalFileName = "sub.ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1
            // 无 Group，无 Language
        };

        vm.MediaFiles.Add(video);
        vm.MediaFiles.Add(subBare);

        // Act
        vm.RecalculateTargetFileName(video);
        vm.RecalculateTargetFileName(subBare);

        // Assert
        subBare.TargetFileName.Should().NotContain("..",
            "无 Group 无 Language 时不应产生连续点号");
        subBare.TargetFileName.Should().EndWith(".ass");
        // 应直接以视频原始基础名 + .ass 结尾
        subBare.TargetFileName.Should().Contain("video");
    }

    /// <summary>
    /// 字幕匹配模式下使用 DefaultSubtitleLanguage 作为后备语言
    /// </summary>
    [Fact]
    public void SubtitleMatching_ShouldUseDefaultLanguage_WhenSubtitleHasNoLanguage()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = true;
        vm.GlobalShowName = "ShowName";
        vm.DefaultSubtitleLanguage = "zh-Hans"; // 设定了默认语言

        var video = new MediaFileItem
        {
            OriginalFileName = "video.mkv",
            Extension = ".mkv",
            FileType = MediaFileType.Video,
            Season = 1,
            Episode = 1,
            Resolution = "1080p"
        };

        var sub = new MediaFileItem
        {
            OriginalFileName = "sub.srt",
            Extension = ".srt",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1,
            ReleaseGroup = "SubGroup"
            // Language is null -> should use DefaultSubtitleLanguage
        };

        vm.MediaFiles.Add(video);
        vm.MediaFiles.Add(sub);

        // Act
        vm.RecalculateTargetFileName(video);
        vm.RecalculateTargetFileName(sub);

        // Assert: 应使用 DefaultSubtitleLanguage
        sub.TargetFileName.Should().EndWith(".SubGroup.zh-Hans.srt",
            "字幕自身没有语言时应使用全局默认字幕语言");
    }

    /// <summary>
    /// 非字幕匹配模式下，字幕文件不应触发专属逻辑
    /// </summary>
    [Fact]
    public void SubtitleFile_ShouldUseNormalLogic_WhenSubtitleMatchingModeIsOff()
    {
        // Arrange
        var vm = new MainViewModel(new MockFileOperationService());
        vm.SubtitleMatchingMode = false; // 关闭字幕匹配模式
        vm.GlobalShowName = "ShowName";

        var sub = new MediaFileItem
        {
            OriginalFileName = "sub.ass",
            Extension = ".ass",
            FileType = MediaFileType.Subtitle,
            Season = 1,
            Episode = 1,
            Language = "zh-Hans",
            ReleaseGroup = "TestGroup"
        };

        vm.MediaFiles.Add(sub);

        // Act
        vm.RecalculateTargetFileName(sub);

        // Assert: 不走字幕匹配逻辑，走常规逻辑（含 tag 组装）
        sub.TargetFileName.Should().Contain("ShowName - S01E01");
        sub.AssociatedVideoItem.Should().BeNull("非字幕匹配模式下不应设置关联视频");
    }
}
