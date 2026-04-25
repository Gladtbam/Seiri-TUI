using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SeiriTUI.Models;
using SeiriTUI.Services;

namespace SeiriTUI.ViewModels;

/// <summary>
/// 全局视图模型，负责全局参数绑定，列表状态维护及所有业务功能执行。
/// 严格满足 MVVM 的单向数据流原则与解耦。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly RegexParsingService _parsingService;
    private readonly IFileOperationService _fileOpsService;

    public MainViewModel(IFileOperationService fileOpsService)
    {
        _parsingService = new RegexParsingService();
        _fileOpsService = fileOpsService;

        // 绑定集合数据，便于 Terminal.Gui 以后对接
        MediaFiles = new ObservableCollection<MediaFileItem>();
    }

    // ================== 全局参数区 (顶部控制区域绑定的属性) ==================

    [ObservableProperty]
    private int _globalSeason = 1;

    [ObservableProperty]
    private int? _startEpisode;

    [ObservableProperty]
    private string _defaultSubtitleLanguage = string.Empty;

    [ObservableProperty]
    private string _targetRootPath = string.Empty;

    [ObservableProperty]
    private string _globalShowName = string.Empty;

    [ObservableProperty]
    private string _globalResolution = string.Empty;

    [ObservableProperty]
    private string _globalQuality = string.Empty;

    [ObservableProperty]
    private bool _autoCreateSeasonFolder = false;

    [ObservableProperty]
    private bool _selectionModeEnabled = false;

    /// <summary>
    /// 工作模式切换：false = 常规媒体整理模式，true = 专属字幕匹配模式
    /// </summary>
    [ObservableProperty]
    private bool _subtitleMatchingMode = false;

    // ================== 文件列表区 ==================

    public ObservableCollection<MediaFileItem> MediaFiles { get; }

    [ObservableProperty]
    private MediaFileItem? _selectedItem;

    [ObservableProperty]
    private string _globalStatusMessage = "Ready";


    // ================== 当任意全局或局部属性变更时，触发重试计算 ==================

    partial void OnGlobalSeasonChanged(int value) => RecalculateAllTargetFileNames();
    partial void OnStartEpisodeChanged(int? value) => RecalculateAllTargetFileNames();
    partial void OnDefaultSubtitleLanguageChanged(string value) => RecalculateAllTargetFileNames();
    partial void OnTargetRootPathChanged(string value) => RecalculateAllTargetFileNames();
    partial void OnGlobalShowNameChanged(string value) => RecalculateAllTargetFileNames();
    partial void OnGlobalResolutionChanged(string value) => RecalculateAllTargetFileNames();
    partial void OnGlobalQualityChanged(string value) => RecalculateAllTargetFileNames();
    partial void OnAutoCreateSeasonFolderChanged(bool value) => RecalculateAllTargetFileNames();
    partial void OnSubtitleMatchingModeChanged(bool value) => RecalculateAllTargetFileNames();

    /// <summary>
    /// 加载并扫描指定目录下的媒体真实文件
    /// </summary>
    public void LoadMediaFilesFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        MediaFiles.Clear();
        var allowedExtensions = new HashSet<string> { ".mp4", ".mkv", ".ts", ".avi", ".mka", ".flac", ".ass", ".srt", ".sup", ".vtt" };

        var files = Directory.GetFiles(directoryPath)
            .Where(f => allowedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => f) // 按字母排序保证集数顺序
            .ToList();

        foreach (var file in files)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            var item = new MediaFileItem
            {
                OriginalPath = file,
                OriginalFileName = Path.GetFileName(file),
                Extension = Path.GetExtension(file),
                FileType = CategorizeFileType(ext)
            };

            _parsingService.Parse(item);
            MediaFiles.Add(item);
        }

        // 解析完毕后统一计算一下目标文件名
        RecalculateAllTargetFileNames();
    }

    /// <summary>
    /// 根据扩展名分类文件类型
    /// </summary>
    public static MediaFileType CategorizeFileType(string extensionLower)
    {
        return extensionLower switch
        {
            ".ass" or ".srt" or ".sup" or ".vtt" => MediaFileType.Subtitle,
            ".mka" or ".flac" => MediaFileType.Audio,
            _ => MediaFileType.Video
        };
    }

    /// <summary>
    /// 当选中的单体独立参数修改时（由 UI / View 侧监听事件推回来，或使用 PropertyChanged 订阅），
    /// 单独更新具体一项的目标文件名。
    /// </summary>
    public void RecalculateTargetFileName(MediaFileItem item)
    {
        // 如果是字幕匹配模式，使用专属字幕命名逻辑
        if (SubtitleMatchingMode && item.FileType == MediaFileType.Subtitle)
        {
            RecalculateSubtitleTargetFileName(item);
            return;
        }

        // 1. 剧集名称：全局参数优先 -> 解析参数 -> UnknownShow
        string showName = !string.IsNullOrWhiteSpace(GlobalShowName)
            ? GlobalShowName
            : (!string.IsNullOrWhiteSpace(item.ParsedShowName) ? item.ParsedShowName : "UnknownShow");

        // 2. 季 (Season)：独立参数 > 全局参数
        int season = item.Season ?? GlobalSeason;

        // 3. 集 (Episode)：如果全局设置了起始集，严格计算顺序递增。否则：独立参数 > 1
        int episode = item.Episode ?? 1;
        if (StartEpisode.HasValue)
        {
            int index = MediaFiles.IndexOf(item);
            if (index >= 0)
            {
                // 找出包括当前文件在内，一共出现过多少个【不同的底层文件组】
                int distinctCount = MediaFiles.Take(index + 1)
                    .Select(f => GetBaseGroupKey(f))
                    .Distinct()
                    .Count();
                
                // 第 N 个不重复的文件组，它的偏移量就应该是 (N - 1)
                episode = StartEpisode.Value + (distinctCount - 1);
            }
        }

        // 4. 基础命名构建：ShowName - S01E01
        string baseName = $"{showName} - S{season:D2}E{episode:D2}";

        // 获取分辨率与质量，采用"独立参数(实体/提取) > 全局参数"的策略
        string finalRes = !string.IsNullOrWhiteSpace(item.Resolution) ? item.Resolution : GlobalResolution;
        string finalQa = !string.IsNullOrWhiteSpace(item.Quality) ? item.Quality : GlobalQuality;

        // 5. 组凑可选标签：如 [WEBDL-1080p][FLAC][x265 10bit]-ReleaseGroup
        string qr = "";
        var qrList = new List<string>();
        if (!string.IsNullOrWhiteSpace(finalQa)) qrList.Add(finalQa);
        if (!string.IsNullOrWhiteSpace(finalRes)) qrList.Add(finalRes);
        if (qrList.Count > 0) qr = $"[{string.Join("-", qrList)}]";

        string ac = "";
        var acList = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.AudioCodec)) acList.Add(item.AudioCodec);
        if (!string.IsNullOrWhiteSpace(item.AudioChannel)) acList.Add(item.AudioChannel);
        if (acList.Count > 0) ac = $"[{string.Join(" ", acList)}]";

        string cb = "";
        var cbList = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.VideoCodec)) cbList.Add(item.VideoCodec);
        if (!string.IsNullOrWhiteSpace(item.BitDepth)) cbList.Add(item.BitDepth);
        if (cbList.Count > 0) cb = $"[{string.Join(" ", cbList)}]";

        string rg = "";
        if (!string.IsNullOrWhiteSpace(item.ReleaseGroup)) rg = $"-{item.ReleaseGroup}";

        string tagString = $"{qr}{ac}{cb}{rg}";
        if (!string.IsNullOrEmpty(tagString)) tagString = $" - {tagString}";

        // 6. 语言后缀：主要应用于外挂字幕 (音轨不需要后缀，即 .mka、.flac 不需要添加 .zh-Hans 后缀)
        string langSuffix = "";
        bool isSubtitle = item.FileType == MediaFileType.Subtitle;

        string lang = !string.IsNullOrWhiteSpace(item.Language) ? item.Language : DefaultSubtitleLanguage;
        if (isSubtitle && !string.IsNullOrWhiteSpace(lang))
        {
            langSuffix = $".{lang}";
        }

        // 最终文件名
        string fileName = $"{baseName}{tagString}{langSuffix}{item.Extension}";

        // 7. 处理父目录 (如果开启了自动创建季文件夹)
        string seasonFolder = AutoCreateSeasonFolder ? $"Season {season:D2}" : "";

        // 赋值给绑定的预览字段 (这里提供相对路径的预览)
        item.TargetFileName = string.IsNullOrEmpty(seasonFolder)
            ? fileName
            : Path.Combine(seasonFolder, fileName);
    }

    /// <summary>
    /// 字幕匹配模式下的专属命名计算：
    /// 格式公式：[匹配的目标视频基础名].[字幕组名].[字幕语言].字幕后缀
    /// </summary>
    private void RecalculateSubtitleTargetFileName(MediaFileItem subtitleItem)
    {
        // 1. 先执行字幕-视频绑定（通过相同的 S/E）
        MatchSubtitleToVideo(subtitleItem);

        // 2. 获取视频基础名称
        string videoBaseName;
        if (subtitleItem.AssociatedVideoItem != null)
        {
            // 先确保关联视频的目标名称是最新的
            var video = subtitleItem.AssociatedVideoItem;
            string videoTargetName = video.TargetFileName;
            if (string.IsNullOrWhiteSpace(videoTargetName))
            {
                // 如果视频的目标文件名还没算过，先算一下
                RecalculateTargetFileName(video);
                videoTargetName = video.TargetFileName;
            }

            // 去除路径前缀 (如 Season 01\xxx.mkv -> xxx.mkv)
            string videoFileName = Path.GetFileName(videoTargetName);
            videoBaseName = Path.GetFileNameWithoutExtension(videoFileName);
        }
        else
        {
            // 未找到匹配的视频，回退为常规命名
            int season = subtitleItem.Season ?? GlobalSeason;
            int episode = subtitleItem.Episode ?? 1;
            string showName = !string.IsNullOrWhiteSpace(GlobalShowName)
                ? GlobalShowName
                : (!string.IsNullOrWhiteSpace(subtitleItem.ParsedShowName) ? subtitleItem.ParsedShowName : "UnknownShow");
            videoBaseName = $"{showName} - S{season:D2}E{episode:D2}";
        }

        // 3. 构建字幕后缀：.[Group].[Lang].ext (缺失智能消除点号)
        var suffixParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subtitleItem.ReleaseGroup))
            suffixParts.Add(subtitleItem.ReleaseGroup);

        string lang = !string.IsNullOrWhiteSpace(subtitleItem.Language)
            ? subtitleItem.Language
            : DefaultSubtitleLanguage;
        if (!string.IsNullOrWhiteSpace(lang))
            suffixParts.Add(lang);

        string suffix = suffixParts.Count > 0 ? "." + string.Join(".", suffixParts) : "";

        string fileName = $"{videoBaseName}{suffix}{subtitleItem.Extension}";

        // 4. 处理父目录
        int finalSeason = subtitleItem.Season ?? GlobalSeason;
        string seasonFolder = AutoCreateSeasonFolder ? $"Season {finalSeason:D2}" : "";

        subtitleItem.TargetFileName = string.IsNullOrEmpty(seasonFolder)
            ? fileName
            : Path.Combine(seasonFolder, fileName);
    }

    /// <summary>
    /// 字幕-视频绑定：通过相同的 Season/Episode 匹配
    /// </summary>
    private void MatchSubtitleToVideo(MediaFileItem subtitleItem)
    {
        int subSeason = subtitleItem.Season ?? GlobalSeason;
        int subEpisode = subtitleItem.Episode ?? 1;

        var matchedVideo = MediaFiles.FirstOrDefault(f =>
            f.FileType == MediaFileType.Video &&
            (f.Season ?? GlobalSeason) == subSeason &&
            (f.Episode ?? 1) == subEpisode);

        subtitleItem.AssociatedVideoItem = matchedVideo;
    }

    private void RecalculateAllTargetFileNames()
    {
        if (SubtitleMatchingMode)
        {
            // 字幕匹配模式：先计算所有视频文件的目标名，再计算字幕文件
            foreach (var item in MediaFiles.Where(f => f.FileType == MediaFileType.Video))
            {
                RecalculateTargetFileName(item);
            }
            foreach (var item in MediaFiles.Where(f => f.FileType != MediaFileType.Video))
            {
                RecalculateTargetFileName(item);
            }
        }
        else
        {
            foreach (var item in MediaFiles)
            {
                RecalculateTargetFileName(item);
            }
        }
    }


    // ================== 勾选操作方法 ==================

    public void SelectAll()
    {
        foreach (var item in MediaFiles) item.IsSelected = true;
    }

    public void DeselectAll()
    {
        foreach (var item in MediaFiles) item.IsSelected = false;
    }

    public void InvertSelection()
    {
        foreach (var item in MediaFiles) item.IsSelected = !item.IsSelected;
    }

    // ================== 执行命令区 (RelayCommand) ==================

    /// <summary>
    /// 检查命令是否可以执行：GlobalSeason 必须 >= 0（必填项校验）
    /// </summary>
    private bool CanExecuteProcess() => GlobalSeason >= 0;

    [RelayCommand(CanExecute = nameof(CanExecuteProcess))]
    private async Task ProcessMoveAsync()
    {
        await ProcessFilesAsync(FileOpMode.Move);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcess))]
    private async Task ProcessCopyAsync()
    {
        await ProcessFilesAsync(FileOpMode.Copy);
    }

    [RelayCommand(CanExecute = nameof(CanExecuteProcess))]
    private async Task ProcessHardLinkAsync()
    {
        await ProcessFilesAsync(FileOpMode.HardLink);
    }

    /// <summary>
    /// 核心批处理逻辑
    /// </summary>
    private async Task ProcessFilesAsync(FileOpMode mode)
    {
        // 1. 检查整体路径
        if (string.IsNullOrWhiteSpace(TargetRootPath))
        {
            GlobalStatusMessage = "错误：未指定目标路径";
            return;
        }

        GlobalStatusMessage = $"开始执行 [{mode}]...";

        int errorCount = 0;

        // 2. 遍历执行（遇到错误不中断，跳过不合条件的项）
        var filesToProcess = MediaFiles.Where(f => !SelectionModeEnabled || f.IsSelected);
        foreach (var item in filesToProcess)
        {
            try
            {
                string finalDestPath = Path.Combine(TargetRootPath, item.TargetFileName);

                await _fileOpsService.ExecuteTransferAsync(item, finalDestPath, mode);
                item.IsProcessed = true;
                item.StatusMessage = "成功";
                item.HasError = false;
            }
            catch (Exception ex)
            {
                // 单个文件失败：只更新单体异常，继续循环
                item.HasError = true;
                item.StatusMessage = $"[{mode}失败]: {ex.Message}";
                errorCount++;
            }
        }

        if (errorCount > 0)
        {
            GlobalStatusMessage = $"处理结束 失败：有 {errorCount} 项遇到如跨盘/权限等错误 (选择列表项查看细节)";
        }
        else
        {
            GlobalStatusMessage = "处理结束：全部成功完成";
        }
    }

    /// <summary>
    /// 提取文件的防重名主键（去除语言后缀），使外挂字幕可以和视频文件匹配成同一集
    /// </summary>
    private static string GetBaseGroupKey(MediaFileItem item)
    {
        if (string.IsNullOrWhiteSpace(item.OriginalFileName)) return "";

        string name = Path.GetFileNameWithoutExtension(item.OriginalFileName);
        string lower = name.ToLowerInvariant();

        string[] suffixes = {
            ".zh-cn", ".zh-tw", ".zh-hk", ".zh-hans", ".zh-hant", ".zh", ".cn",
            ".en", ".eng", ".chs", ".cht", ".tc", ".sc", ".jp", ".jap", ".kr", ".kor",
            ".ru", ".rus", ".spa", ".fre", ".ger", ".ita", ".chi", ".zho",
            ".default", ".forced", ".cc", ".sdh", ".jptc", ".jpsc"
        };

        bool stripped = true;
        while (stripped)
        {
            stripped = false;
            foreach (var suffix in suffixes)
            {
                if (lower.EndsWith(suffix))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    lower = name.ToLowerInvariant();
                    stripped = true;
                    break;
                }
            }
        }

        // 去除残留的点、横杠或空格（比如用户写了 "jptc" 没加点导致残留的 '.'）
        return lower.TrimEnd('.', ' ', '-', '_');
    }
}
