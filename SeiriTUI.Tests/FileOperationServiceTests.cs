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

/// <summary>
/// IO 文件操作与安全边界测试 (FileOperationService & Mocking)
/// 必须通过注入 IFileService 等接口使用 Mock 框架进行测试，严禁在单元测试中进行真实磁盘写操作。
/// </summary>
public class FileOperationServiceTests
{
    /// <summary>
    /// 跨盘符硬链接异常拦截测试：
    /// 模拟底层抛出跨盘硬链接 IOException，断言程序能够正确捕获异常，
    /// 返回失败状态，且绝不触发文件移动 (Move) 或删除操作。
    /// </summary>
    [Fact]
    public async Task CrossDriveHardLink_ShouldCatchException_AndNeverFallbackToMove()
    {
        // Arrange: 使用 NSubstitute 模拟 IFileOperationService
        var mockFileService = Substitute.For<IFileOperationService>();
        
        var fileItem = new MediaFileItem
        {
            OriginalPath = "C:\\source\\video.mkv",
            OriginalFileName = "video.mkv",
            TargetFileName = "output.mkv",
            Extension = ".mkv"
        };

        // 模拟跨盘硬链接 IOException
        mockFileService.ExecuteTransferAsync(
            Arg.Any<MediaFileItem>(),
            Arg.Any<string>(),
            FileOpMode.HardLink
        ).ThrowsAsync(new IOException("跨盘符不支持硬链接 (C:\\ -> D:\\)"));

        var vm = new MainViewModel(mockFileService);
        vm.MediaFiles.Add(fileItem);
        vm.TargetRootPath = "D:\\target";

        // Act
        await vm.ProcessHardLinkCommand.ExecuteAsync(null);

        // Assert: 文件应标记为出错
        fileItem.HasError.Should().BeTrue("跨盘硬链接应正确标记为错误状态");
        fileItem.StatusMessage.Should().Contain("跨盘符不支持硬链接");
        fileItem.IsProcessed.Should().BeFalse("出错的文件不应标记为已处理");

        // 确保绝不触发 Move 操作 (不降级)
        await mockFileService.DidNotReceive().ExecuteTransferAsync(
            Arg.Any<MediaFileItem>(),
            Arg.Any<string>(),
            FileOpMode.Move
        );
    }

    /// <summary>
    /// 无干扰错误反馈机制测试：
    /// 传递包含 3 个文件的批量任务，模拟第 2 个文件遇到权限拒绝抛出异常。
    /// 断言第 1 和第 3 个文件操作成功，且整个批处理方法未崩溃并返回包含第 2 个文件报错信息的错误列表。
    /// </summary>
    [Fact]
    public async Task BatchProcess_ShouldNotCrash_WhenMiddleFileFails()
    {
        // Arrange：使用手写 Mock，模拟第 2 个文件 (index=1) 抛异常
        var mockIo = new MockFileOperationService
        {
            FailOnIndex = new HashSet<int> { 1 } // 第 2 个文件失败
        };
        var vm = new MainViewModel(mockIo);

        var item1 = new MediaFileItem { OriginalFileName = "file1.mkv", TargetFileName = "out1.mkv", Extension = ".mkv" };
        var item2 = new MediaFileItem { OriginalFileName = "file2.mkv", TargetFileName = "out2.mkv", Extension = ".mkv" };
        var item3 = new MediaFileItem { OriginalFileName = "file3.mkv", TargetFileName = "out3.mkv", Extension = ".mkv" };

        vm.MediaFiles.Add(item1);
        vm.MediaFiles.Add(item2);
        vm.MediaFiles.Add(item3);
        vm.TargetRootPath = "/fake/target";

        // Act：执行批量操作应不崩溃
        await vm.ProcessCopyCommand.ExecuteAsync(null);

        // Assert: 第 1 和第 3 个文件操作成功
        item1.IsProcessed.Should().BeTrue("第 1 个文件应成功处理");
        item1.HasError.Should().BeFalse();

        item3.IsProcessed.Should().BeTrue("第 3 个文件应成功处理");
        item3.HasError.Should().BeFalse();

        // 第 2 个文件应标记出错
        item2.HasError.Should().BeTrue("第 2 个文件应标记为出错");
        item2.IsProcessed.Should().BeFalse();
        item2.StatusMessage.Should().Contain("Simulated strict IO Error");

        // Mock 实际成功处理了 2 个文件
        mockIo.TransferRecords.Should().HaveCount(2);
    }

    /// <summary>
    /// 目标路径自动创建测试：
    /// Mock 文件系统返回"目标路径不存在"，触发操作命令，
    /// 断言是否正确调用了 CreateDirectory 且路径格式包含设定的 Season 01。
    /// </summary>
    [Fact]
    public async Task Execute_ShouldPassCorrectSeasonPath_WhenAutoCreateEnabled()
    {
        // Arrange: 使用 NSubstitute
        var mockFileService = Substitute.For<IFileOperationService>();
        var vm = new MainViewModel(mockFileService);

        vm.AutoCreateSeasonFolder = true;
        vm.GlobalSeason = 1;
        vm.TargetRootPath = "/target/root";

        var item = new MediaFileItem
        {
            ParsedShowName = "TestShow",
            Episode = 1,
            Extension = ".mkv",
            FileType = MediaFileType.Video
        };

        vm.MediaFiles.Add(item);
        vm.RecalculateTargetFileName(item);

        // Act
        await vm.ProcessMoveCommand.ExecuteAsync(null);

        // Assert: 传递给底层服务的路径应包含 Season 01
        await mockFileService.Received(1).ExecuteTransferAsync(
            item,
            Arg.Is<string>(path => path.Contains("Season 01")),
            FileOpMode.Move
        );
    }

    /// <summary>
    /// 原文件安全保护断言：
    /// 模拟在执行文件移动或复制的中途触发"磁盘空间不足"异常，
    /// 断言原文件状态为未受损/未删除（即异常被捕获、item 标记为错误但不标记已处理）。
    /// </summary>
    [Fact]
    public async Task OriginalFileSafety_ShouldPreserveOriginal_WhenDiskSpaceInsufficient()
    {
        // Arrange
        var mockFileService = Substitute.For<IFileOperationService>();

        var item = new MediaFileItem
        {
            OriginalPath = "/source/original.mkv",
            OriginalFileName = "original.mkv",
            TargetFileName = "target.mkv",
            Extension = ".mkv"
        };

        // 模拟磁盘空间不足异常
        mockFileService.ExecuteTransferAsync(
            Arg.Any<MediaFileItem>(),
            Arg.Any<string>(),
            FileOpMode.Move
        ).ThrowsAsync(new IOException("磁盘空间不足"));

        var vm = new MainViewModel(mockFileService);
        vm.MediaFiles.Add(item);
        vm.TargetRootPath = "/target";

        // Act
        await vm.ProcessMoveCommand.ExecuteAsync(null);

        // Assert: 原文件未被标记为已处理（意味着不应被删除）
        item.HasError.Should().BeTrue("异常应被捕获并标记错误");
        item.IsProcessed.Should().BeFalse("发生磁盘错误时文件不应标记为已处理");
        item.StatusMessage.Should().Contain("磁盘空间不足");

        // ViewModel 不应崩溃
        vm.GlobalStatusMessage.Should().Contain("失败");
    }

    /// <summary>
    /// 使用 NSubstitute 验证权限错误时完整的批处理流程
    /// </summary>
    [Fact]
    public async Task BatchProcess_WithNSubstitute_ShouldHandlePermissionDenied()
    {
        // Arrange
        var mockFileService = Substitute.For<IFileOperationService>();

        var item1 = new MediaFileItem { OriginalFileName = "ok1.mkv", TargetFileName = "out1.mkv", Extension = ".mkv" };
        var item2 = new MediaFileItem { OriginalFileName = "denied.mkv", TargetFileName = "out2.mkv", Extension = ".mkv" };
        var item3 = new MediaFileItem { OriginalFileName = "ok3.mkv", TargetFileName = "out3.mkv", Extension = ".mkv" };

        // 仅第 2 个文件抛出权限异常
        mockFileService.ExecuteTransferAsync(item1, Arg.Any<string>(), Arg.Any<FileOpMode>())
            .Returns(Task.CompletedTask);
        mockFileService.ExecuteTransferAsync(item2, Arg.Any<string>(), Arg.Any<FileOpMode>())
            .ThrowsAsync(new UnauthorizedAccessException("权限拒绝"));
        mockFileService.ExecuteTransferAsync(item3, Arg.Any<string>(), Arg.Any<FileOpMode>())
            .Returns(Task.CompletedTask);

        var vm = new MainViewModel(mockFileService);
        vm.MediaFiles.Add(item1);
        vm.MediaFiles.Add(item2);
        vm.MediaFiles.Add(item3);
        vm.TargetRootPath = "/target";

        // Act
        await vm.ProcessCopyCommand.ExecuteAsync(null);

        // Assert
        item1.IsProcessed.Should().BeTrue();
        item1.HasError.Should().BeFalse();

        item2.HasError.Should().BeTrue();
        item2.StatusMessage.Should().Contain("权限拒绝");

        item3.IsProcessed.Should().BeTrue();
        item3.HasError.Should().BeFalse();
    }
}
