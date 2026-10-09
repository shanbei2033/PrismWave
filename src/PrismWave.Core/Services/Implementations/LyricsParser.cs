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
            parsed.Sort((left, right) => left.TimeSeconds.CompareTo(right.TimeSeconds));
            var finalized = FinalizeKaraokeSegments(parsed);
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
