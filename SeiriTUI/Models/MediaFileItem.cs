using CommunityToolkit.Mvvm.ComponentModel;

namespace SeiriTUI.Models;

/// <summary>
/// 文件类型枚举：区分视频、字幕和外挂音轨
/// </summary>
public enum MediaFileType
{
    Video,
    Subtitle,
    Audio
}

/// <summary>
/// 表示核心业务实体：媒体文件项。
/// 包含原文件信息，以及独立且优先级最高的重命名参数。
/// </summary>
public partial class MediaFileItem : ObservableObject
{
    // ====== 原始文件信息 (不可变) ======

    /// <summary>原始文件完整路径</summary>
    public string OriginalPath { get; init; } = string.Empty;

    /// <summary>原始文件名称包含扩展名</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>纯扩展名 (如 .mkv)</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>文件类型 (视频/字幕/音轨)</summary>
    public MediaFileType FileType { get; init; } = MediaFileType.Video;

    /// <summary>字幕匹配模式下：关联的视频文件项 (通过相同 S/E 匹配)</summary>
    [ObservableProperty]
    private MediaFileItem? _associatedVideoItem;


    // ====== 解析/覆盖 参数 (可被 ViewModel 更新或用户 UI 独立修改) ======

    /// <summary>自动提取的疑似剧集名</summary>
    [ObservableProperty]
    private string? _parsedShowName;

    [ObservableProperty]
    private int? _season;

    [ObservableProperty]
    private int? _episode;

    [ObservableProperty]
    private string? _quality;

    [ObservableProperty]
    private string? _resolution;

    /// <summary>主要针对字幕/外挂音轨的语言标识</summary>
    [ObservableProperty]
    private string? _language;

    /// <summary>编码格式 (如 x265, x264)</summary>
    [ObservableProperty]
    private string? _videoCodec;

    /// <summary>音频编码格式 (如 FLAC, AAC, AC3)</summary>
    [ObservableProperty]
    private string? _audioCodec;

    /// <summary>音频声道 (如 5.1, 2.0)</summary>
    [ObservableProperty]
    private string? _audioChannel;

    /// <summary>色彩深度 (如 8bit, 10bit, 12bit)</summary>
    [ObservableProperty]
    private string? _bitDepth;

    [ObservableProperty]
    private bool _isSelected = false;

    /// <summary>压制组/发布组 (如 VCB-Studio, Beatrice-Raws)</summary>
    [ObservableProperty]
    private string? _releaseGroup;


    // ====== 输出状态 ======

    /// <summary>目标重命名预览（由 ViewModel 根据当前属性与全局属性计算后更新）</summary>
    [ObservableProperty]
    private string _targetFileName = string.Empty;

    /// <summary>处理状态信息（报错信息展示）</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>标记是否发生错误（用于 UI 标红）</summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>标记是否成功处理</summary>
    [ObservableProperty]
    private bool _isProcessed;
}
