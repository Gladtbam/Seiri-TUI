using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SeiriTUI.Models;

namespace SeiriTUI.Services;

public enum FileOpMode
{
    Move,
    Copy,
    HardLink
}

/// <summary>
/// 服务：专职进行 IO 读写、硬链接等操作；拦截诸如不同盘符和权限错误。
/// </summary>
public class FileOperationService
{
    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", SetLastError = true)]
    private static extern int link(string oldpath, string newpath);

    public FileOperationService() { }

    /// <summary>
    /// 执行转移核心动作
    /// 不包含抛异常后的 UI 中断，把异常发向外汇聚
    /// </summary>
    public async Task ExecuteTransferAsync(MediaFileItem fileItem, string finalPath, FileOpMode mode)
    {
        string? dir = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            // 目标路径校验：由上层决定是否中断单文件操作
            Directory.CreateDirectory(dir);
        }

        string sourceRoot = GetVolumeRoot(Path.GetFullPath(fileItem.OriginalPath));
        string targetRoot = GetVolumeRoot(Path.GetFullPath(finalPath));

        switch (mode)
        {
            case FileOpMode.Copy:
                // 运行在后台线程以避免卡死 UI
                await Task.Run(() => File.Copy(fileItem.OriginalPath, finalPath, true));
                break;
            case FileOpMode.Move:
                await Task.Run(() => {
                    if (File.Exists(finalPath)) File.Delete(finalPath);
                    // 保证原文件安全(Zero Corruption)：跨盘移动使用安全拷贝后删除机制
                    if (sourceRoot != targetRoot)
                    {
                        File.Copy(fileItem.OriginalPath, finalPath, true);
                        File.Delete(fileItem.OriginalPath);
                    }
                    else
                    {
                        File.Move(fileItem.OriginalPath, finalPath);
                    }
                });
                break;
            case FileOpMode.HardLink:
                // 硬链接跨盘校验：直接报错，绝不默默降级移动，破坏保种
                if (sourceRoot != targetRoot)
                {
                    throw new IOException($"跨盘符不支持硬链接 ({sourceRoot} -> {targetRoot})");
                }
                await Task.Run(() => CreateHardLinkInternal(fileItem.OriginalPath, finalPath));
                break;
        }
    }

    /// <summary>
    /// 精准获取路径所在的物理分区/挂载点根目录 (兼容 Windows/Linux/macOS)
    /// </summary>
    private string GetVolumeRoot(string fullPath)
    {
        try
        {
            // .NET DriveInfo 原生支持跨平台将全路径解析为底层 Mount/Drive 的 Name
            return new DriveInfo(fullPath).Name;
        }
        catch 
        {
            // Fallback，防止路径本身畸形导致 DriveInfo 实例化失败
            return Path.GetPathRoot(fullPath)?.ToLowerInvariant() ?? "";
        }
    }

    private void CreateHardLinkInternal(string source, string target)
    {
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            bool success = CreateHardLink(target, source, IntPtr.Zero);
            if (!success)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"跨盘符无法硬链接或权限错误 (CreateHardLink failed, err: {error})");
            }
        }
        else
        {
            // MacOS / Linux
            int result = link(source, target);
            if (result != 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException($"跨盘符无法硬链接或权限错误 (link failed, err: {error})");
            }
        }
    }
}
