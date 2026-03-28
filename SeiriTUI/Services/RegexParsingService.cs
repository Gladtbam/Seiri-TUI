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
    
    // 匹配 S01, Season 1 等
    [GeneratedRegex(@"(?i)(?:s|season)[\s\._\-]*(\d+)", RegexOptions.Compiled)]
    private static partial Regex SeasonRegex();

    // 匹配 E01, EP01, [01], - 01 等 (排除压制组等干扰)
    [GeneratedRegex(@"(?i)(?:e|ep|episode)[\s\._\-]*(\d+)|(?<=\s|^|\[|-)0*(\d{1,4})(?=\s|$|\]|-)", RegexOptions.Compiled)]
    private static partial Regex EpisodeRegex();

    // 匹配分辨率 (1080p, 2160p, 4k, 720p)
    [GeneratedRegex(@"(?i)(1080[pi]|720[pi]|2160[pi]|4k|8k)", RegexOptions.Compiled)]
    private static partial Regex ResolutionRegex();

    // 匹配来源/质量 (BluRay, BDRip, WEB-DL, WEBRip, HDTV)
    [GeneratedRegex(@"(?i)(bluray|bdrip|web-?dl|web-?rip|hdtv|dvdrip)", RegexOptions.Compiled)]
    private static partial Regex QualityRegex();

    // 匹配编码 (x264, x265, h264, h265, hevc, avc)
    [GeneratedRegex(@"(?i)(x264|x265|h264|h265|hevc|avc)", RegexOptions.Compiled)]
    private static partial Regex VideoCodecRegex();

    // 匹配色彩深度 (8bit, 10bit, 12bit)
    [GeneratedRegex(@"(?i)(8bit|10bit|12bit)", RegexOptions.Compiled)]
    private static partial Regex BitDepthRegex();

    // 匹配发布组/压制组 (通常在文件最开头的方括号里 [VCB-Studio] ...)
    [GeneratedRegex(@"^\[([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex ReleaseGroupRegex();

    public RegexParsingService() { }

    /// <summary>
    /// 对传入的实体对象，应用一系列正则表达式提取参数
    /// </summary>
    public void Parse(MediaFileItem item)
    {
        string name = Path.GetFileNameWithoutExtension(item.OriginalFileName);
        
        // 1. 尝试匹配压制组/发布组
        var rgMatch = ReleaseGroupRegex().Match(name);
        if (rgMatch.Success)
        {
            item.ReleaseGroup = rgMatch.Groups[1].Value;
        }

        // 2. 尝试匹配季数 (Season)
        var seasonMatch = SeasonRegex().Match(name);
        if (seasonMatch.Success && int.TryParse(seasonMatch.Groups[1].Value, out int s))
        {
            item.Season = s;
        }

        // 3. 尝试匹配集数 (Episode) 
        // 应对多种模式，需要剔除已经被识别为季数的部分或者单独处理 S01E02 中的 E02 
        var epMatch = EpisodeRegex().Match(name);
        if (epMatch.Success)
        {
            string epStr = string.IsNullOrEmpty(epMatch.Groups[1].Value) ? epMatch.Groups[2].Value : epMatch.Groups[1].Value;
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
            item.BitDepth = bitDepthMatch.Groups[1].Value.ToLower(); // 统一为 10bit
        }

        // 8. 解析字幕外挂语言等
        item.Language = ParseLanguage(item.OriginalFileName);
        
        // 9. 智能尝试猜测剧集名 (掐头去尾)
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
            // 去除可能遗留的下划线或短横线
            show = Regex.Replace(show, @"^[-_]+|[-_]+$", "").Trim();
            if (!string.IsNullOrEmpty(show)) return show;
        }
        return null;
    }

    /// <summary>
    /// 标准化字幕语言代码 (CHS -> zh-Hans)
    /// </summary>
    private string? ParseLanguage(string fileName)
    {
        string nameLower = fileName.ToLowerInvariant();
        
        if (Regex.IsMatch(nameLower, @"(chs|gb|sc|zh-hans|简体)")) return "zh-Hans";
        if (Regex.IsMatch(nameLower, @"(cht|big5|tc|zh-hant|繁体|正體)")) return "zh-Hant";
        if (Regex.IsMatch(nameLower, @"(chs.*jp|jp.*chs|简日)")) return "zh-Hans"; 
        if (Regex.IsMatch(nameLower, @"(cht.*jp|jp.*cht|繁日)")) return "zh-Hant";
        if (Regex.IsMatch(nameLower, @"(eng|en|english)")) return "eng";
        if (Regex.IsMatch(nameLower, @"(jap|jp|ja|japanese|日本語)")) return "jpn";

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
        if (low == "hevc" || low == "h265") return "x265";
        if (low == "avc" || low == "h264") return "x264";
        return low;
    }
}
