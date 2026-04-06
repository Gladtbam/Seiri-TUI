using System.IO;
using System.Text.RegularExpressions;
using SeiriTUI.Models;

namespace SeiriTUI.Services;

/// <summary>
/// 服务：专职负责文件名正则提取，包括匹配语言标识、集数、分辨率等信息。
/// </summary>
public partial class RegexParsingService
{
    // ====== 编译期的正则表达式 (性能优化) ======
    // 匹配 S01, Season 1 等，支持 第X季
    [GeneratedRegex(@"(?i)(?:s|season)[\s\._\-]*(\d+)|第\s*(\d+)\s*季", RegexOptions.Compiled)]
    private static partial Regex SeasonRegex();

    // 匹配 E01, EP01, [01], - 01 等，支持 第X集/话/話, 也支持 OVA01 等 (排除压制组等干扰)
    [GeneratedRegex(@"(?i)(?:e|ep|episode|ova|oad|sp|special)[\s\._\-]*(\d+)|第\s*(\d+)\s*(?:话|話|集)|(?<=\s|^|\[|-)0*(\d{1,4})(?=\s|$|\]|-)", RegexOptions.Compiled)]
    private static partial Regex EpisodeRegex();

    // 匹配分辨率 (1080p, 2160p, 4k, 720p, 1920x1080等)
    [GeneratedRegex(@"(?i)(1080[pi]|720[pi]|2160[pi]|4k|8k|1920x1080|1280x720|3840x2160)", RegexOptions.Compiled)]
    private static partial Regex ResolutionRegex();

    // 匹配来源/质量 (BluRay, BDRip, WEB-DL, WEBRip, HDTV, BD)
    [GeneratedRegex(@"(?i)(bluray|bdrip|web-?dl|web-?rip|hdtv|dvdrip|\bbd\b)", RegexOptions.Compiled)]
    private static partial Regex QualityRegex();

    // 匹配编码 (x264, x265, h264, h265, hevc, avc, h.264, h.265)
    [GeneratedRegex(@"(?i)(x264|x265|h\.?264|h\.?265|hevc|avc)", RegexOptions.Compiled)]
    private static partial Regex VideoCodecRegex();

    // 匹配色彩深度 (8bit, 10bit, 12bit, p8, p10)
    [GeneratedRegex(@"(?i)(8bit|10bit|12bit|p8|p10)", RegexOptions.Compiled)]
    private static partial Regex BitDepthRegex();

    // 匹配音频编码 (FLAC, AAC, AC3, DTS, TrueHD, OPUS, MP3, EAC3)
    [GeneratedRegex(@"(?i)(flac|aac|ac3|eac3|dts|truehd|ddp|opus|mp3)", RegexOptions.Compiled)]
    private static partial Regex AudioCodecRegex();

    // 匹配音频声道 (7.1, 5.1, 2.1, 2.0)
    [GeneratedRegex(@"(?i)(?<!\d)(7\.1|5\.1|2\.1|2\.0)(?!\d)", RegexOptions.Compiled)]
    private static partial Regex AudioChannelRegex();

    // 匹配发布组/压制组 (通常在文件最开头的方括号里 [VCB-Studio] ...)
    [GeneratedRegex(@"^\[([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex ReleaseGroupRegex();

    // 匹配后缀括号发布组 (如 -[SubsPlease] 或 [SubsPlease] 在结尾)
    [GeneratedRegex(@"(?:- ?\[|\[)([^\]]+)\]$", RegexOptions.Compiled)]
    private static partial Regex SuffixBracketReleaseGroupRegex();

    // 匹配后缀横杠发布组 (如 -NTb 在结尾)
    [GeneratedRegex(@"-([a-zA-Z][a-zA-Z0-9_-]*)$", RegexOptions.Compiled)]
    private static partial Regex SuffixDashReleaseGroupRegex();

    public RegexParsingService() { }

    /// <summary>
    /// 对传入的实体对象，应用一系列正则表达式提取参数
    /// </summary>
    public void Parse(MediaFileItem item)
    {
        string name = Path.GetFileNameWithoutExtension(item.OriginalFileName);

        // 1. 尝试匹配前置压制组/发布组
        var rgMatch = ReleaseGroupRegex().Match(name);
        if (rgMatch.Success)
        {
            item.ReleaseGroup = rgMatch.Groups[1].Value.Trim();
        }
        else
        {
            // 尝试匹配后置发布组 (方括号风格)
            var suffixBracketMatch = SuffixBracketReleaseGroupRegex().Match(name);
            if (suffixBracketMatch.Success)
            {
                item.ReleaseGroup = suffixBracketMatch.Groups[1].Value.Trim();
            }
            else
            {
                // 尝试匹配后置发布组 (横向风格)
                var suffixDashMatch = SuffixDashReleaseGroupRegex().Match(name);
                if (suffixDashMatch.Success)
                {
                    item.ReleaseGroup = suffixDashMatch.Groups[1].Value.Trim();
                }
            }
        }

        // 2. 尝试匹配季数 (Season)
        var seasonMatch = SeasonRegex().Match(name);
        if (seasonMatch.Success)
        {
            string sStr = seasonMatch.Groups[1].Success ? seasonMatch.Groups[1].Value : seasonMatch.Groups[2].Value;
            if (int.TryParse(sStr, out int s))
            {
                item.Season = s;
            }
        }
        else if (Regex.IsMatch(name, @"(?i)(?:\b|_|-)(ova|oad|sp|special)(?:\b|_|-|0*\d+)"))
        {
            // 如果未明确标明包含季数，但包含了 OVA、OAD 等特殊篇标识，默认归类为第 0 季
            item.Season = 0;
        }

        // 3. 尝试匹配集数 (Episode) 
        // 应对多种模式，需要剔除已经被识别为季数的部分或者单独处理 S01E02 中的 E02 
        var epMatch = EpisodeRegex().Match(name);
        if (epMatch.Success)
        {
            string epStr = epMatch.Groups[1].Success ? epMatch.Groups[1].Value :
                           epMatch.Groups[2].Success ? epMatch.Groups[2].Value :
                           epMatch.Groups[3].Value;
            if (int.TryParse(epStr, out int e))
            {
                item.Episode = e;
            }
        }

        // 4. 分辨率
        var resMatch = ResolutionRegex().Match(name);
        if (resMatch.Success)
        {
            item.Resolution = StandardizeResolution(resMatch.Groups[1].Value);
        }

        // 5. 质量/来源
        var qualityMatch = QualityRegex().Match(name);
        if (qualityMatch.Success)
        {
            // 标准化，如 WEB-DL
            item.Quality = StandardizeQuality(qualityMatch.Groups[1].Value);
        }

        // 6. 编码
        var codecMatch = VideoCodecRegex().Match(name);
        if (codecMatch.Success)
        {
            item.VideoCodec = StandardizeCodec(codecMatch.Groups[1].Value);
        }

        // 7. 色彩深度
        var bitDepthMatch = BitDepthRegex().Match(name);
        if (bitDepthMatch.Success)
        {
            string b = bitDepthMatch.Groups[1].Value.ToLower();
            item.BitDepth = b == "p10" ? "10bit" :
                            b == "p8" ? "8bit" : b;
        }

        // 8. 音频编码
        var audioMatch = AudioCodecRegex().Match(name);
        if (audioMatch.Success)
        {
            item.AudioCodec = StandardizeAudioCodec(audioMatch.Groups[1].Value);
        }

        // 8.5 音频声道
        var channelMatch = AudioChannelRegex().Match(name);
        if (channelMatch.Success)
        {
            item.AudioChannel = channelMatch.Groups[1].Value;
        }

        // 9. 解析字幕外挂语言等
        item.Language = ParseLanguage(item.OriginalFileName);

        // 10. 智能尝试猜测剧集名 (掐头去尾)
        item.ParsedShowName = GuessShowName(name);
    }

    private string? GuessShowName(string name)
    {
        // 抹除首个发布组
        var groupMatch = ReleaseGroupRegex().Match(name);
        int startIndex = groupMatch.Success ? groupMatch.Length : 0;

        // 寻找第一次出现集号或季号，或者新方括号的地方
        var epMatch = EpisodeRegex().Match(name, startIndex);
        var seasonMatch = SeasonRegex().Match(name, startIndex);
        int firstBracket = name.IndexOf('[', startIndex);

        int endIdx = name.Length;
        if (epMatch.Success && epMatch.Index < endIdx) endIdx = epMatch.Index;
        if (seasonMatch.Success && seasonMatch.Index < endIdx) endIdx = seasonMatch.Index;
        if (firstBracket != -1 && firstBracket < endIdx) endIdx = firstBracket;

        if (endIdx > startIndex)
        {
            string show = name.Substring(startIndex, endIdx - startIndex).Trim();
            // 还原 Scene 格式下被 . 替代的空格
            show = show.Replace(".", " ");
            // 去除可能遗留的下划线或短横线
            show = Regex.Replace(show, @"^[-_]+|[-_]+$", "").Trim();
            // 处理多个连续空格的情况
            show = Regex.Replace(show, @"\s+", " ").Trim();

            if (!string.IsNullOrEmpty(show)) return show;
        }
        return null;
    }

    /// <summary>
    /// 标准化字幕语言代码 (CHS -> zh-Hans)
    /// </summary>
    private string? ParseLanguage(string fileName)
    {
        // 去除拓展名，纯看名称主体，并转小写
        string nameLower = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        // 辅助检测函数：语言标识符往往被标点符号包围 (如 .en. / [TC] / -chs-)
        // (?:\W|_|^) 代表前面必须是一个特殊的非字母数字字符（括号中划线点等），或是开头
        // (?:\W|_|$) 代表后面必须是一个特殊的非字母数字字符，或是结尾
        bool IsLang(string pattern)
        {
            return Regex.IsMatch(nameLower, $@"(?:\W|_|^){pattern}(?:\W|_|$)");
        }

        if (IsLang("(chs.*jp|jp.*chs|jp.*sc|简日)")) return "zh-Hans";
        if (IsLang("(cht.*jp|jp.*cht|jp.*tc|繁日)")) return "zh-Hant";
        if (IsLang("(chs|gb|sc|zh-hans|简体|简中)")) return "zh-Hans";
        if (IsLang("(cht|big5|tc|zh-hant|繁体|正體|繁中)")) return "zh-Hant";
        if (IsLang("(eng|en|english)")) return "eng";
        if (IsLang("(jap|jp|ja|japanese|日本語)")) return "jpn";

        return null;
    }

    private string StandardizeQuality(string original)
    {
        string low = original.ToLowerInvariant();
        if (low.Contains("web-dl") || low == "webdl") return "WEBDL";
        if (low.Contains("webrip")) return "WEBRip";
        if (low.Contains("bdrip")) return "BDRip";
        if (low.Contains("bluray") || low == "bd") return "BD";
        if (low.Contains("hdtv")) return "HDTV";
        return original;
    }

    private string StandardizeResolution(string original)
    {
        string low = original.ToLowerInvariant();
        if (low.Contains("4k") || low.Contains("2160")) return "2160p";
        if (low.Contains("8k") || low.Contains("4320")) return "4320p";
        if (low.Contains("1080")) return "1080p";
        if (low.Contains("720")) return "720p";
        if (low.Contains("480")) return "480p";

        // uppercase the 'p' if needed or keep standard lowercase 'P' 
        return original;
    }

    private string StandardizeCodec(string original)
    {
        string low = original.ToLowerInvariant();
        if (low == "hevc" || low == "h265" || low == "h.265") return "x265";
        if (low == "avc" || low == "h264" || low == "h.264") return "x264";
        return low;
    }

    private string StandardizeAudioCodec(string original)
    {
        string low = original.ToLowerInvariant();
        if (low == "ddp" || low == "eac3") return "E-AC-3";
        if (low == "truehd") return "TrueHD";
        if (low == "ac3") return "AC-3";
        if (low == "aac") return "AAC";
        if (low == "flac") return "FLAC";
        if (low == "dts") return "DTS";
        if (low == "opus") return "OPUS";
        if (low == "mp3") return "MP3";
        return original.ToUpperInvariant();
    }
}
