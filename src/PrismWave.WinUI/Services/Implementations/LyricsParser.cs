using System.Net;
using System.Text.RegularExpressions;
using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Services.Implementations;

public static class LyricsParser
{
    private static readonly Regex TimeTagPattern = new(
        @"\[(?<minute>\d{1,2}):(?<second>\d{2})(?:[\.:](?<fraction>\d{1,3}))?\]",
        RegexOptions.Compiled);

    private static readonly Regex KaraokeTimeTagPattern = new(
        @"<(?<minute>\d{1,2}):(?<second>\d{2})(?:[\.:](?<fraction>\d{1,3}))?>",
        RegexOptions.Compiled);

    private static readonly Regex QrcLinePattern = new(
        @"^\[(?<start>\d+),(?<duration>\d+)\](?<body>.*)$",
        RegexOptions.Compiled);

    private static readonly Regex QrcWordPattern = new(
        @"(?:\[\d+,\d+\])?((?:(?!\(\d+,\d+\)).)*)\((\d+),(\d+)\)",
        RegexOptions.Compiled);

    private static readonly Regex YrcWordPattern = new(
        @"\((?<start>\d+),(?<duration>\d+),\d+\)(?<text>.*?)(?=\(\d+,\d+,\d+\)|$)",
        RegexOptions.Compiled);

    private static readonly Regex KrcWordPattern = new(
        @"(?<text>.*?)<(?<start>\d+),(?<duration>\d+),\d+>",
        RegexOptions.Compiled);

    private static readonly Regex QrcAttributePattern = new(
        "LyricContent=(?:\"(?<double>[\\s\\S]*?)\"|'(?<single>[\\s\\S]*?)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex QrcCdataPattern = new(
        @"<LyricContent><!\[CDATA\[(?<content>[\s\S]*?)\]\]></LyricContent>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static LyricsDocumentModel Parse(
        string raw,
        double durationHintSeconds = 0,
        string source = "local",
        string provider = "local")
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return LyricsDocumentModel.Empty(source);
        }

        var yrcLines = ParseYrc(raw);
        if (yrcLines.Count > 0)
        {
            return new LyricsDocumentModel(yrcLines, source, provider, true, raw);
        }

        var krcLines = ParseKrc(raw);
        if (krcLines.Count > 0)
        {
            return new LyricsDocumentModel(krcLines, source, provider, true, raw);
        }

        var qrcLines = ParseQrc(raw);
        if (qrcLines.Count > 0)
        {
            return new LyricsDocumentModel(qrcLines, source, provider, true, raw);
        }

        var parsed = new List<LyricLineModel>();
        var plain = new List<string>();
        foreach (var rawLine in NormalizeLines(raw))
        {
            var matches = TimeTagPattern.Matches(rawLine);
            if (matches.Count > 0)
            {
                var segments = ParseKaraokeSegments(rawLine);
                var text = segments.Count > 0
                    ? string.Concat(segments.Select(segment => segment.Text))
                    : KaraokeTimeTagPattern.Replace(TimeTagPattern.Replace(rawLine, string.Empty), string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                foreach (Match match in matches)
                {
                    var seconds = ReadTimestamp(match);
                    parsed.Add(new LyricLineModel(seconds, FormatTime(seconds), text, segments));
                }

                continue;
            }

            if (IsMetadataLine(rawLine))
            {
                continue;
            }

            var plainText = KaraokeTimeTagPattern.Replace(rawLine, string.Empty).Trim();
            if (plainText.Length > 0)
            {
                plain.Add(plainText);
            }
        }

        if (parsed.Count > 0)
        {
            var sorted = parsed.OrderBy(line => line.TimeSeconds).ToList();
            var merged = MergeSameTimestampLines(sorted);
            var absorbed = AbsorbBackingVocalLines(merged);
            var finalized = FinalizeKaraokeSegments(absorbed);
            return new LyricsDocumentModel(finalized, source, provider, true, raw);
        }

        if (plain.Count == 0)
        {
            return LyricsDocumentModel.Empty(source);
        }

        var totalSeconds = durationHintSeconds > 0 ? durationHintSeconds : plain.Count * 3.2;
        var stepSeconds = Math.Clamp(totalSeconds / plain.Count, 1.2, 8);
        var lines = plain
            .Select((text, index) => new LyricLineModel(stepSeconds * index, string.Empty, text))
            .ToList();
        return new LyricsDocumentModel(lines, source, provider, false, raw);
    }

    private static IReadOnlyList<LyricLineModel> ParseYrc(string raw)
    {
        var lines = new List<LyricLineModel>();
        foreach (var rawLine in NormalizeLines(raw))
        {
            var lineMatch = QrcLinePattern.Match(rawLine.Trim());
            if (!lineMatch.Success)
            {
                continue;
            }

            var segments = YrcWordPattern.Matches(lineMatch.Groups["body"].Value)
                .Select(match =>
                {
                    var start = ReadMilliseconds(match.Groups["start"].Value);
                    var duration = ReadMilliseconds(match.Groups["duration"].Value);
                    return new LyricSegmentModel(
                        start,
                        start + Math.Max(duration, 0.01),
                        match.Groups["text"].Value);
                })
                .Where(segment => segment.Text.Length > 0 && segment.Text != "\r")
                .ToList();
            if (segments.Count == 0)
            {
                continue;
            }

            var lineStart = ReadMilliseconds(lineMatch.Groups["start"].Value);
            lines.Add(new LyricLineModel(
                lineStart,
                FormatTime(lineStart),
                string.Concat(segments.Select(segment => segment.Text)),
                segments));
        }

        lines.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        return lines;
    }

    private static IReadOnlyList<LyricLineModel> ParseKrc(string raw)
    {
        var lines = new List<LyricLineModel>();
        foreach (var rawLine in NormalizeLines(raw))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var lineMatch = QrcLinePattern.Match(line);
            if (!lineMatch.Success)
            {
                continue;
            }

            var body = lineMatch.Groups["body"].Value;
            var wordMatches = KrcWordPattern.Matches(body);
            if (wordMatches.Count == 0)
            {
                continue;
            }

            var segments = new List<LyricSegmentModel>();
            var textBuilder = new System.Text.StringBuilder();
            foreach (Match match in wordMatches)
            {
                var text = match.Groups["text"].Value;
                if (text.Length == 0)
                {
                    continue;
                }

                var start = ReadMilliseconds(match.Groups["start"].Value);
                var duration = ReadMilliseconds(match.Groups["duration"].Value);
                segments.Add(new LyricSegmentModel(start, start + Math.Max(duration, 0.01), text));
                textBuilder.Append(text);
            }

            if (segments.Count == 0 || textBuilder.Length == 0)
            {
                continue;
            }

            var lineStart = ReadMilliseconds(lineMatch.Groups["start"].Value);
            lines.Add(new LyricLineModel(
                lineStart,
                FormatTime(lineStart),
                textBuilder.ToString(),
                segments));
        }

        lines.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        return lines;
    }

    private static IReadOnlyList<LyricLineModel> ParseQrc(string raw)
    {
        var content = ExtractQrcContent(raw);
        if (string.IsNullOrWhiteSpace(content))
        {
            return Array.Empty<LyricLineModel>();
        }

        var lines = new List<LyricLineModel>();
        foreach (var rawLine in NormalizeLines(content))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || IsMetadataLine(line))
            {
                continue;
            }

            var lineMatch = QrcLinePattern.Match(line);
            if (!lineMatch.Success)
            {
                continue;
            }

            var lineStart = ReadMilliseconds(lineMatch.Groups["start"].Value);
            var body = lineMatch.Groups["body"].Value;
            var segments = new List<LyricSegmentModel>();
            foreach (Match match in QrcWordPattern.Matches(body))
            {
                var text = match.Groups[1].Value;
                if (text.Length == 0 || text == "\r")
                {
                    continue;
                }

                var start = ReadMilliseconds(match.Groups[2].Value);
                var duration = ReadMilliseconds(match.Groups[3].Value);
                segments.Add(new LyricSegmentModel(start, start + duration, text));
            }

            var lineText = segments.Count > 0
                ? string.Concat(segments.Select(segment => segment.Text))
                : body.Trim();
            if (lineText.Length == 0)
            {
                continue;
            }

            lines.Add(new LyricLineModel(lineStart, FormatTime(lineStart), lineText, segments));
        }

        lines.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
        return lines;
    }

    private static string? ExtractQrcContent(string raw)
    {
        var trimmed = raw.Trim();
        if (QrcLinePattern.IsMatch(trimmed))
        {
            return trimmed;
        }

        var attribute = QrcAttributePattern.Match(trimmed);
        if (attribute.Success)
        {
            var encoded = attribute.Groups["double"].Success
                ? attribute.Groups["double"].Value
                : attribute.Groups["single"].Value;
            return WebUtility.HtmlDecode(encoded);
        }

        var cdata = QrcCdataPattern.Match(trimmed);
        return cdata.Success ? cdata.Groups["content"].Value : null;
    }

    private static IReadOnlyList<LyricSegmentModel> ParseKaraokeSegments(string rawLine)
    {
        var body = TimeTagPattern.Replace(rawLine, string.Empty);
        var matches = KaraokeTimeTagPattern.Matches(body);
        if (matches.Count == 0)
        {
            return Array.Empty<LyricSegmentModel>();
        }

        var segments = new List<LyricSegmentModel>();
        for (var index = 0; index < matches.Count; index++)
        {
            var current = matches[index];
            var textStart = current.Index + current.Length;
            var textEnd = index + 1 < matches.Count ? matches[index + 1].Index : body.Length;
            if (textEnd <= textStart)
            {
                continue;
            }

            var text = body[textStart..textEnd];
            if (text.Length == 0)
            {
                continue;
            }

            var start = ReadTimestamp(current);
            segments.Add(new LyricSegmentModel(start, start, text));
        }

        return segments;
    }

    private static IReadOnlyList<LyricLineModel> MergeSameTimestampLines(List<LyricLineModel> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        var merged = new List<LyricLineModel>(lines.Count);
        var index = 0;
        while (index < lines.Count)
        {
            var groupStart = index;
            var time = lines[index].TimeSeconds;
            index++;
            while (index < lines.Count && Math.Abs(lines[index].TimeSeconds - time) < 0.001)
            {
                index++;
            }

            var groupCount = index - groupStart;
            if (groupCount == 1)
            {
                merged.Add(lines[groupStart]);
                continue;
            }

            merged.Add(MergeLineGroup(lines, groupStart, groupCount));
        }

        return merged;
    }

    private static LyricLineModel MergeLineGroup(List<LyricLineModel> lines, int start, int count)
    {
        var group = new List<LyricLineModel>(count);
        for (var offset = 0; offset < count; offset++)
        {
            group.Add(lines[start + offset]);
        }

        var wordTimed = group.FirstOrDefault(line => line.HasTimedSegments);
        if (wordTimed is not null)
        {
            // 逐字段主行的文本不可改动（逐字索引依赖），其余行全部进副行。
            var companions = CollectCompanions(group, wordTimed);
            return companions.Count == 0 ? wordTimed : wordTimed with { CompanionLines = companions };
        }

        var scriptCount = group
            .Select(line => ClassifyScript(line.Text))
            .Distinct()
            .Count();
        if (scriptCount >= 2)
        {
            // 双语组（如日文原文 + 罗马音）：首个非拉丁行为主行，其余为副行。
            var primaryIndex = group.FindIndex(line => ClassifyScript(line.Text) != "latin");
            if (primaryIndex < 0)
            {
                primaryIndex = 0;
            }

            var primary = group[primaryIndex];
            var companions = CollectCompanions(group, primary);
            return companions.Count == 0 ? primary : primary with { CompanionLines = companions };
        }

        // 同脚本拆句（同一句拆多短行）：拼接为一句，保证舞台高亮稳定。
        var joinedText = string.Join(" ", group
            .Select(line => line.Text)
            .Where(text => text.Length > 0));
        return group[0] with { Text = joinedText };
    }

    private static List<LyricCompanionModel> CollectCompanions(List<LyricLineModel> group, LyricLineModel primary)
    {
        var companions = new List<LyricCompanionModel>(primary.CompanionLines ?? Array.Empty<LyricCompanionModel>());
        foreach (var line in group)
        {
            if (ReferenceEquals(line, primary) || line.Text.Length == 0)
            {
                continue;
            }

            companions.Add(new LyricCompanionModel(line.Text));
        }

        return companions;
    }

    private static string ClassifyScript(string text)
    {
        foreach (var character in text)
        {
            if (character >= '\u3040' && character <= '\u30FF')
            {
                return "kana";
            }
        }

        foreach (var character in text)
        {
            if ((character >= '\u4E00' && character <= '\u9FFF') || (character >= '\u3400' && character <= '\u4DBF'))
            {
                return "han";
            }
        }

        foreach (var character in text)
        {
            if (character >= '\uAC00' && character <= '\uD7AF')
            {
                return "hangul";
            }
        }

        return "latin";
    }

    private static IReadOnlyList<LyricLineModel> AbsorbBackingVocalLines(IReadOnlyList<LyricLineModel> lines)
    {
        if (lines.Count < 2)
        {
            return lines;
        }

        var result = new List<LyricLineModel>(lines.Count);
        foreach (var line in lines)
        {
            if (!IsBackingVocalText(line.Text))
            {
                result.Add(line);
                continue;
            }

            // 括号和声行并入演唱区间重叠的前置主行，避免抢占当前行高亮。
            var absorbed = false;
            for (var hostIndex = result.Count - 1; hostIndex >= 0; hostIndex--)
            {
                var host = result[hostIndex];
                var hostEnd = GetLineEndSeconds(host);
                if (line.TimeSeconds < host.TimeSeconds - 0.001)
                {
                    break;
                }

                if (line.TimeSeconds <= hostEnd + 0.35)
                {
                    var companions = new List<LyricCompanionModel>(host.CompanionLines ?? Array.Empty<LyricCompanionModel>())
                    {
                        new(line.Text, FinalizeCompanionSegments(line.TimedSegments))
                    };
                    result[hostIndex] = host with { CompanionLines = companions };
                    absorbed = true;
                    break;
                }
            }

            if (!absorbed)
            {
                result.Add(line);
            }
        }

        return result;
    }

    private static IReadOnlyList<LyricSegmentModel> FinalizeCompanionSegments(IReadOnlyList<LyricSegmentModel> segments)
    {
        if (segments.Count == 0)
        {
            return Array.Empty<LyricSegmentModel>();
        }

        // 和声行被吸收后不再经过主行 Finalize，这里自行补全段结束：
        // 下一字起始优先，末字给固定点亮时长。
        var finalized = new List<LyricSegmentModel>(segments.Count);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var nextStart = index + 1 < segments.Count
                ? segments[index + 1].StartSeconds
                : segment.StartSeconds + 0.4;
            var safeEnd = nextStart > segment.StartSeconds ? nextStart : segment.StartSeconds + 0.4;
            finalized.Add(segment with { EndSeconds = safeEnd });
        }

        return finalized;
    }

    private static bool IsBackingVocalText(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 && trimmed[0] == '(' && trimmed[^1] == ')';
    }

    private static double GetLineEndSeconds(LyricLineModel line)
    {
        var end = line.TimeSeconds;
        foreach (var segment in line.TimedSegments)
        {
            if (segment.EndSeconds > end)
            {
                end = segment.EndSeconds;
            }
        }

        return end;
    }

    private static IReadOnlyList<LyricLineModel> FinalizeKaraokeSegments(IReadOnlyList<LyricLineModel> lines)
    {
        if (lines.All(line => !line.HasTimedSegments))
        {
            return lines;
        }

        var finalized = new List<LyricLineModel>(lines.Count);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (!line.HasTimedSegments)
            {
                finalized.Add(line);
                continue;
            }

            var nextLineTime = lineIndex + 1 < lines.Count
                ? lines[lineIndex + 1].TimeSeconds
                : line.TimeSeconds + 3;
            var segments = new List<LyricSegmentModel>(line.TimedSegments.Count);
            for (var segmentIndex = 0; segmentIndex < line.TimedSegments.Count; segmentIndex++)
            {
                var segment = line.TimedSegments[segmentIndex];
                var nextStart = segmentIndex + 1 < line.TimedSegments.Count
                    ? line.TimedSegments[segmentIndex + 1].StartSeconds
                    : nextLineTime;
                var safeEnd = nextStart > segment.StartSeconds ? nextStart : segment.StartSeconds + 0.12;
                segments.Add(segment with { EndSeconds = safeEnd });
            }

            finalized.Add(line with { Segments = segments });
        }

        return finalized;
    }

    private static IEnumerable<string> NormalizeLines(string raw)
    {
        return raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    private static bool IsMetadataLine(string raw)
    {
        var line = raw.Trim();
        return line.StartsWith("[ti:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("[ar:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("[al:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("[by:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase);
    }

    private static double ReadTimestamp(Match match)
    {
        _ = int.TryParse(match.Groups["minute"].Value, out var minutes);
        _ = int.TryParse(match.Groups["second"].Value, out var seconds);
        var fractionText = match.Groups["fraction"].Value;
        _ = int.TryParse(fractionText, out var fraction);
        var milliseconds = fractionText.Length switch
        {
            1 => fraction * 100,
            2 => fraction * 10,
            >= 3 => fraction,
            _ => 0
        };
        return minutes * 60 + seconds + milliseconds / 1000d;
    }

    private static double ReadMilliseconds(string value)
    {
        return double.TryParse(value, out var milliseconds) ? milliseconds / 1000d : 0;
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }
}
