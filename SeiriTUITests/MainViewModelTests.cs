using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SeiriTUI.Models;
using SeiriTUI.Services;
using SeiriTUI.ViewModels;
using Xunit;

namespace SeiriTUITests;

// 手写一个极为轻量的 Mock 用于规避真实的磁盘 IO
public class MockFileOperationService : IFileOperationService
{
    public List<(MediaFileItem FileItem, string FinalPath, FileOpMode Mode)> TransferRecords { get; } = new();
    
    // 如果想要模拟发生跨盘或其他磁盘异常，开启此开关
    public bool SimulateException { get; set; } = false;

    public Task ExecuteTransferAsync(MediaFileItem fileItem, string finalPath, FileOpMode mode)
    {
        if (SimulateException)
        {
            throw new Exception("Simulated strict IO Error (e.g., cross-drive hardlink)");
        }
        
        TransferRecords.Add((fileItem, finalPath, mode));
        return Task.CompletedTask;
    }
}

public class MainViewModelTests
{
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
        
        // Act: 模拟我们在界面上点击了“执行重命名(移动)”
        await vm.ProcessMoveCommand.ExecuteAsync(null);

        // Assert: 验证是否干净利落地把请求甩给了底层 IO 服务
        Assert.Equal(2, mockIo.TransferRecords.Count);
        
        // 校验底层拿到的请求对不对
        Assert.Equal(fakeItem1, mockIo.TransferRecords[0].FileItem);
        Assert.Equal(System.IO.Path.Combine("/fake/target/dir", fakeItem1.TargetFileName).Replace("/", "\\"), mockIo.TransferRecords[0].FinalPath.Replace("/", "\\"));
        Assert.Equal(FileOpMode.Move, mockIo.TransferRecords[0].Mode);
        
        // 校验文件自身的状态属性是否正确刷新为已处理和无错误
        Assert.True(fakeItem1.IsProcessed);
        Assert.False(fakeItem1.HasError);
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
        Assert.True(badItem.HasError);
        Assert.Contains("Simulated strict IO Error", badItem.StatusMessage);
        
        // 这个文件处理失败了，状态不能被标记作“成功已处理”
        Assert.False(badItem.IsProcessed);
    }
}
