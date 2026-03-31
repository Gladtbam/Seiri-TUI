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
/// <summary>
/// 服务：专职进行 IO 读写、硬链接等操作；拦截诸如不同盘符和权限错误。
/// </summary>
public class FileOperationService : IFileOperationService
{
    [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

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
            Directory.CreateDirectory(dir);
        }

        string sourceRoot = GetVolumeRoot(Path.GetFullPath(fileItem.OriginalPath));
        string targetRoot = GetVolumeRoot(Path.GetFullPath(finalPath));

        // 判断是否跨盘符/跨挂载点
        bool isCrossDrive = !string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase);

        switch (mode)
        {
            case FileOpMode.Copy:
                // 运行在后台线程以避免卡死 UI
                await Task.Run(() => File.Copy(fileItem.OriginalPath, finalPath, true));
                break;

            case FileOpMode.Move:
                await Task.Run(() =>
                {
                    if (File.Exists(finalPath)) File.Delete(finalPath);

                    // 保证原文件安全(Zero Corruption)：跨盘移动使用安全拷贝后删除机制
                    if (isCrossDrive)
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
                if (isCrossDrive)
                {
                    throw new IOException($"跨盘符不支持硬链接 ({sourceRoot} -> {targetRoot})");
                }
                await Task.Run(() => CreateHardLinkInternal(fileItem.OriginalPath, finalPath));
                break;
        }
    }

    /// <summary>
    /// 精准获取路径所在的物理分区/挂载点根目录 (兼容 Windows/Linux/macOS)
    /// 修复了 Linux 统一下返回 "/" 的问题
    /// </summary>
    private string GetVolumeRoot(string fullPath)
    {
        try
        {
            string targetPath = Path.GetFullPath(fullPath);
            string bestMatch = string.Empty;

            // Windows 盘符不区分大小写，Linux 挂载点区分大小写
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            StringComparison comp = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            // 遍历系统中所有已挂载的驱动器
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                string root = drive.RootDirectory.FullName;

                // 如果文件的完整路径是以该挂载点开头
                if (targetPath.StartsWith(root, comp))
                {
                    // 严格判定：防止类似 /mnt/data 错误匹配到 /mnt/data2/file 的情况
                    bool isValidMatch = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ||
                                        targetPath.Length == root.Length ||
                                        targetPath[root.Length] == Path.DirectorySeparatorChar;

                    if (isValidMatch && root.Length > bestMatch.Length)
                    {
                        // 寻找匹配长度最长的挂载点 (比如 /mnt/data 优先于 /)
                        bestMatch = root;
                    }
                }
            }

            if (!string.IsNullOrEmpty(bestMatch))
            {
                return bestMatch;
            }

            // Fallback (极少触发)
            return Path.GetPathRoot(fullPath) ?? "";
        }
        catch
        {
            return Path.GetPathRoot(fullPath) ?? "";
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
                throw new IOException($"Windows硬链接创建失败或权限不足 (err: {error})");
            }
        }
        else
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "ln";

            process.StartInfo.ArgumentList.Add(source);
            process.StartInfo.ArgumentList.Add(target);

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string errorMsg = process.StandardError.ReadToEnd().Trim();
                throw new IOException($"跨盘符无法硬链接或权限错误 (ln 命令失败, ExitCode: {process.ExitCode}): {errorMsg}");
            }
        }
    }
}