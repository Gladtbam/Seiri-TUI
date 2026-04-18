using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SeiriTUI.Models;
using SeiriTUI.Services;
using SeiriTUI.ViewModels;
using Xunit;

namespace SeiriTUI.Tests;

// 手写一个极为轻量的 Mock 用于规避真实的磁盘 IO (保留向后兼容)
public class MockFileOperationService : IFileOperationService
{
    public List<(MediaFileItem FileItem, string FinalPath, FileOpMode Mode)> TransferRecords { get; } = new();
    
    // 如果想要模拟发生跨盘或其他磁盘异常，开启此开关
    public bool SimulateException { get; set; } = false;

    /// <summary>
    /// 可以设定仅对指定索引的文件抛异常
    /// </summary>
    public HashSet<int> FailOnIndex { get; set; } = new();

    private int _callCount = 0;

    public Task ExecuteTransferAsync(MediaFileItem fileItem, string finalPath, FileOpMode mode)
    {
        int currentIndex = _callCount++;

        if (SimulateException || FailOnIndex.Contains(currentIndex))
        {
            throw new IOException("Simulated strict IO Error (e.g., cross-drive hardlink)");
        }
        
        TransferRecords.Add((fileItem, finalPath, mode));
        return Task.CompletedTask;
    }
}

/// <summary>
/// ViewModel 状态逻辑及命令绑定测试
/// </summary>
public class MainViewModelTests
{
    // ==================== 执行范围过滤测试 ====================

    [Fact]
    public async Task ProcessFiles_ShouldCallMockIO_WithoutHittingRealDisk()
    {
        // Arrange: 注入我们自制的 Mock 库
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        
        // 我们虚构两个文件待处理，并且没有物理路径
        var fakeItem1 = new MediaFileItem { OriginalFileName = "Video.mkv", TargetFileName = "ParsedVideo.mkv" };
        var fakeItem2 = new MediaFileItem { OriginalFileName = "Subtitle.ass", TargetFileName = "ParsedSubtitle.ass" };
        vm.MediaFiles.Add(fakeItem1);
        vm.MediaFiles.Add(fakeItem2);
        
        // 设置合法的输出目录 (规避拦截检查)
        vm.TargetRootPath = "/fake/target/dir";
        
        // Act: 模拟我们在界面上点击了"执行重命名(移动)"
        await vm.ProcessMoveCommand.ExecuteAsync(null);

        // Assert: 验证是否干净利落地把请求甩给了底层 IO 服务
        mockIo.TransferRecords.Should().HaveCount(2);
        
        // 校验底层拿到的请求对不对
        mockIo.TransferRecords[0].FileItem.Should().BeSameAs(fakeItem1);
        mockIo.TransferRecords[0].Mode.Should().Be(FileOpMode.Move);
        
        // 校验文件自身的状态属性是否正确刷新为已处理和无错误
        fakeItem1.IsProcessed.Should().BeTrue();
        fakeItem1.HasError.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessFiles_ShouldNotCrashApplication_WhenSingleFileThrowsException()
    {
        // Arrange
        var mockIo = new MockFileOperationService { SimulateException = true }; // 开启模拟异常模式
        var vm = new MainViewModel(mockIo);
        
        var badItem = new MediaFileItem { OriginalFileName = "CorruptedFile.mp4", TargetFileName = "Output.mp4" };
        vm.MediaFiles.Add(badItem);
        vm.TargetRootPath = "D:/mock/dir";
        
        // Act
        // 如果 ViewModel 里的 try-catch 失效，这里会直接抛出异常导致进程崩溃 (测试失败)
        await vm.ProcessHardLinkCommand.ExecuteAsync(null);

        // Assert: 虽然遭遇异常，但因为捕获了所以不会崩，而会在实体上标记带有 Error
        badItem.HasError.Should().BeTrue();
        badItem.StatusMessage.Should().Contain("Simulated strict IO Error");
        
        // 这个文件处理失败了，状态不能被标记作"成功已处理"
        badItem.IsProcessed.Should().BeFalse();
    }

    /// <summary>
    /// 全量执行断言 + 勾选过滤断言
    /// </summary>
    [Fact]
    public async Task ProcessFiles_ShouldRespectSelectionMode_WhenSelectionModeIsEnabled()
    {
        // Arrange
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        
        var selectedItem = new MediaFileItem { OriginalFileName = "1.mkv", TargetFileName = "out1.mkv", IsSelected = true };
        var unselectedItem = new MediaFileItem { OriginalFileName = "2.mkv", TargetFileName = "out2.mkv", IsSelected = false };
        
        vm.MediaFiles.Add(selectedItem);
        vm.MediaFiles.Add(unselectedItem);
        vm.TargetRootPath = "/fake/";
        
        // Act - 1. SelectionModeEnabled = false (应全部执行)
        vm.SelectionModeEnabled = false;
        await vm.ProcessMoveCommand.ExecuteAsync(null);
        mockIo.TransferRecords.Should().HaveCount(2, "SelectionMode 关闭时，所有文件应全部被处理");
        
        // Act - 2. SelectionModeEnabled = true (应仅执行已选项)
        mockIo.TransferRecords.Clear();
        selectedItem.IsProcessed = false;
        unselectedItem.IsProcessed = false;
        
        vm.SelectionModeEnabled = true;
        await vm.ProcessMoveCommand.ExecuteAsync(null);
        
        // Assert: 只有 1 项应该被处理，未选中的项触发次数为 0
        mockIo.TransferRecords.Should().ContainSingle();
        mockIo.TransferRecords[0].FileItem.OriginalFileName.Should().Be("1.mkv");
    }

    [Fact]
    public void SelectAll_ShouldMarkAllItemsAsSelected()
    {
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "a.mkv", IsSelected = false });
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "b.mkv", IsSelected = false });
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "c.mkv", IsSelected = true });

        vm.SelectAll();

        vm.MediaFiles.Should().OnlyContain(item => item.IsSelected);
    }

    [Fact]
    public void DeselectAll_ShouldUnmarkAllItems()
    {
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "a.mkv", IsSelected = true });
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "b.mkv", IsSelected = true });

        vm.DeselectAll();

        vm.MediaFiles.Should().OnlyContain(item => !item.IsSelected);
    }

    [Fact]
    public void InvertSelection_ShouldToggleEachItem()
    {
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "a.mkv", IsSelected = true });
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "b.mkv", IsSelected = false });
        vm.MediaFiles.Add(new MediaFileItem { OriginalFileName = "c.mkv", IsSelected = true });

        vm.InvertSelection();

        vm.MediaFiles[0].IsSelected.Should().BeFalse();
        vm.MediaFiles[1].IsSelected.Should().BeTrue();
        vm.MediaFiles[2].IsSelected.Should().BeFalse();
    }

    // ==================== 优先级与覆盖机制测试 ====================

    /// <summary>
    /// 必须项与可选项组装断言 (核心要求) - 全量数据
    /// </summary>
    [Fact]
    public void RecalculateTargetFileName_ShouldAssembleCorrectly_WithFullData()
    {
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        
        var item = new MediaFileItem
        {
            ParsedShowName = "ShowName",
            Season = 1,
            Episode = 1,
            Quality = "BDRip",
            Resolution = "1080p",
            AudioCodec = "AAC",
            AudioChannel = "2.0",
            VideoCodec = "x265",
            BitDepth = "10bit",
            ReleaseGroup = "ReleaseGroup",
            Extension = ".mkv"
        };
        
        vm.RecalculateTargetFileName(item);
        
        item.TargetFileName.Should().Be("ShowName - S01E01 - [BDRip-1080p][AAC 2.0][x265 10bit]-ReleaseGroup.mkv");
    }

    /// <summary>
    /// 必须项与可选项组装断言 (核心要求) - 块级缺失
    /// </summary>
    [Fact]
    public void RecalculateTargetFileName_ShouldOmitBlocks_WhenDataMissing()
    {
        var mockIo = new MockFileOperationService();
        var vm = new MainViewModel(mockIo);
        
        var item = new MediaFileItem
        {
            ParsedShowName = "ShowName",
            Season = 1,
            Episode = 1,
            Quality = "BDRip",
            Resolution = "1080p",
            VideoCodec = "x265",
            BitDepth = "10bit",
            Extension = ".mkv"
            // Missing AudioCodec, AudioChannel, ReleaseGroup
        };
        
        vm.RecalculateTargetFileName(item);
        
        // 音频组和发布组缺失时，不应出现多余空格或空括号
        item.TargetFileName.Should().Be("ShowName - S01E01 - [BDRip-1080p][x265 10bit].mkv");
    }

    /// <summary>
    /// 属性优先级断言：独立参数 > 全局参数 > 正则提取
    /// </summary>
    [Fact]
    public void RecalculateTargetFileName_ShouldRespectPriority_Independent_Global_Parsed()
    {
        var vm = new MainViewModel(new MockFileOperationService());
        vm.GlobalSeason = 2; // Global
        vm.GlobalShowName = "GlobalShow"; // Global
        
        var item = new MediaFileItem
        {
            ParsedShowName = "ParsedShow",
            Season = 1, // Independent
            Episode = 5,
            Extension = ".mkv"
        };
        
        vm.RecalculateTargetFileName(item);
        
        // ShowName uses Global (higher priority than Parsed), Season uses Independent (highest priority)
        item.TargetFileName.Should().StartWith("GlobalShow - S01E05");
        
        // Remove Independent Season -> Should fallback to Global
        item.Season = null;
        vm.RecalculateTargetFileName(item);
        item.TargetFileName.Should().StartWith("GlobalShow - S02E05");
    }

    /// <summary>
    /// 起始集自增覆盖测试
    /// </summary>
    [Fact]
    public void ProcessCommand_ShouldIncrementEpisodes_WhenStartEpisodeIsSet()
    {
        var vm = new MainViewModel(new MockFileOperationService());
        vm.StartEpisode = 10;
        
        var item1 = new MediaFileItem { OriginalFileName = "E01.mkv", Extension = ".mkv" };
        var item2 = new MediaFileItem { OriginalFileName = "E01.ass", Extension = ".ass" }; // Same group
        var item3 = new MediaFileItem { OriginalFileName = "E02.mkv", Extension = ".mkv" }; // Different group
        var item4 = new MediaFileItem { OriginalFileName = "Random.mkv", Extension = ".mkv" }; // Third group
        
        vm.MediaFiles.Add(item1);
        vm.MediaFiles.Add(item2);
        vm.MediaFiles.Add(item3);
        vm.MediaFiles.Add(item4);
        
        vm.RecalculateTargetFileName(item1);
        vm.RecalculateTargetFileName(item2);
        vm.RecalculateTargetFileName(item3);
        vm.RecalculateTargetFileName(item4);
        
        // item1 and item2: 1st group -> 10
        item1.TargetFileName.Should().Contain("E10");
        item2.TargetFileName.Should().Contain("E10");
        // item3: 2nd group -> 11
        item3.TargetFileName.Should().Contain("E11");
        // item4: 3rd group -> 12
        item4.TargetFileName.Should().Contain("E12");
    }

    /// <summary>
    /// 全局剧集名称修改为空后回退到正则解析的备用名称
    /// </summary>
    [Fact]
    public void RegressGlobalShowName_ShouldFallbackToParsedShowName()
    {
        var vm = new MainViewModel(new MockFileOperationService());
        var item = new MediaFileItem { ParsedShowName = "Parsed", Episode = 1, Extension = ".mkv" };
        
        vm.GlobalShowName = "Global";
        vm.RecalculateTargetFileName(item);
        item.TargetFileName.Should().StartWith("Global");
        
        // Remove Global ShowName
        vm.GlobalShowName = "";
        vm.RecalculateTargetFileName(item);
        // Should fallback to Parsed Show Name
        item.TargetFileName.Should().StartWith("Parsed");
    }

    /// <summary>
    /// 外挂字幕/音频绑定测试：重命名预览时外挂文件继承视频基础名且正确加上语言后缀
    /// </summary>
    [Fact]
    public void RecalculateTargetFileName_ShouldAddLanguageSuffixToSubtitles()
    {
        var vm = new MainViewModel(new MockFileOperationService());
        vm.DefaultSubtitleLanguage = "zh-CN";
        
        var subItem = new MediaFileItem
        {
            ParsedShowName = "Show", Episode = 1, Language = "zh-Hans",
            Extension = ".ass", FileType = MediaFileType.Subtitle
        };
        var subItemNoLang = new MediaFileItem
        {
            ParsedShowName = "Show", Episode = 1,
            Extension = ".srt", FileType = MediaFileType.Subtitle
        };
        var videoItem = new MediaFileItem
        {
            ParsedShowName = "Show", Episode = 1, Language = "zh-Hans",
            Extension = ".mkv", FileType = MediaFileType.Video
        };
        
        vm.RecalculateTargetFileName(subItem);
        vm.RecalculateTargetFileName(subItemNoLang);
        vm.RecalculateTargetFileName(videoItem);
        
        // Subtitle with parsed lang
        subItem.TargetFileName.Should().EndWith(".zh-Hans.ass");
        
        // Subtitle without parsed lang -> Use DefaultSubtitleLanguage
        subItemNoLang.TargetFileName.Should().EndWith(".zh-CN.srt");
        
        // Video file -> Never append language suffix
        videoItem.TargetFileName.Should().EndWith("E01.mkv");
    }

    // ==================== 命令执行与边界输入测试 ====================

    /// <summary>
    /// AutoCreateSeasonFolder 启用时目标路径包含 Season 文件夹
    /// </summary>
    [Fact]
    public void RecalculateTargetFileName_ShouldIncludeSeasonFolder_WhenAutoCreateEnabled()
    {
        var vm = new MainViewModel(new MockFileOperationService());
        vm.AutoCreateSeasonFolder = true;
        vm.GlobalSeason = 2;
        
        var item = new MediaFileItem { ParsedShowName = "Show", Episode = 1, Extension = ".mkv" };
        vm.RecalculateTargetFileName(item);
        
        item.TargetFileName.Should().StartWith("Season 02");
        item.TargetFileName.Should().Contain("Show - S02E01");
    }
}
