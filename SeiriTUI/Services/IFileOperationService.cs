using System.Threading.Tasks;
using SeiriTUI.Models;

namespace SeiriTUI.Services;

/// <summary>
/// 服务接口：专职进行 IO 读写、硬链接等操作
/// </summary>
public interface IFileOperationService
{
    /// <summary>
    /// 执行转移核心动作
    /// </summary>
    Task ExecuteTransferAsync(MediaFileItem fileItem, string finalPath, FileOpMode mode);
}
