using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace PrismWave_WinUI.Models;

public sealed record LyricSegmentModel(
    double StartSeconds,
    double EndSeconds,
    string Text);

public sealed record LyricLineModel(
    double TimeSeconds,
    string TimeLabel,
    string Text,
    IReadOnlyList<LyricSegmentModel>? Segments = null)
{
    public IReadOnlyList<LyricSegmentModel> TimedSegments => Segments ?? Array.Empty<LyricSegmentModel>();
    public bool HasTimedSegments => TimedSegments.Count > 0;
}

public enum LyricsSelectionKind
{
    Auto,
    Manual
}

public sealed record LyricsDocumentModel(
    IReadOnlyList<LyricLineModel> Lines,
    string Source,
    string Provider,
    bool IsSynced,
    string? RawText = null,
    LyricsSelectionKind SelectionKind = LyricsSelectionKind.Auto)
{
    public bool IsEmpty => Lines.Count == 0;
    public bool HasTimedSegments => Lines.Any(line => line.HasTimedSegments);

    public static LyricsDocumentModel Empty(string source = "none") => new(
        Array.Empty<LyricLineModel>(),
        source,
        source,
        false);
}

public enum LyricsSyncKind
{
    Plain,
    LineSynced,
    WordSynced
}

public enum LyricsTransitionKind
{
    Initial,
    Natural,
    Rapid
}

public sealed record LyricsSearchResultModel(
    string Id,
    string TrackName,
    string ArtistName,
    string AlbumName,
    double DurationSeconds,
    string? SyncedLyrics,
    string? PlainLyrics,
    string Provider)
{
    private static readonly Regex QrcWordTimingPattern = new(
        @"\(\d+,\d+\)",
        RegexOptions.Compiled);
    private static readonly Regex EnhancedWordTimingPattern = new(
        @"<\d{1,2}:\d{2}(?:[\.:]\d{1,3})?>",
        RegexOptions.Compiled);

    public string DisplayTitle => string.IsNullOrWhiteSpace(ArtistName)
        ? TrackName
        : $"{TrackName} · {ArtistName}";

    public LyricsSyncKind LyricsKind => string.IsNullOrWhiteSpace(SyncedLyrics)
        ? LyricsSyncKind.Plain
        : QrcWordTimingPattern.IsMatch(SyncedLyrics)
          || EnhancedWordTimingPattern.IsMatch(SyncedLyrics)
            ? LyricsSyncKind.WordSynced
            : LyricsSyncKind.LineSynced;

    public int LyricsQualityRank => (int)LyricsKind;

    public string LyricsKindLabel => LyricsKind switch
    {
        LyricsSyncKind.WordSynced => "逐字",
        LyricsSyncKind.LineSynced => "逐行",
        _ => "纯文本"
    };
}

public sealed partial class LyricLineDisplayModel : ObservableObject
{
    private bool _isCurrent;
    private double _wordProgress;
    private int _distanceFromCurrent = int.MaxValue;
    private bool _isManualBrowsing;
    private LyricsTransitionKind _transitionKind = LyricsTransitionKind.Initial;

    public LyricLineDisplayModel(LyricLineModel line)
    {
        Line = line;
    }

    public LyricLineModel Line { get; }
    public double TimeSeconds => Line.TimeSeconds;
    public string TimeLabel => Line.TimeLabel;
    public string Text => Line.Text;
    public IReadOnlyList<LyricSegmentModel> Segments => Line.TimedSegments;

    public LyricsTransitionKind TransitionKind
    {
        get => _transitionKind;
        set => SetProperty(ref _transitionKind, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (SetProperty(ref _isCurrent, value))
            {
                OnPropertyChanged(nameof(TextOpacity));
                OnPropertyChanged(nameof(TextSize));
            }
        }
    }

    public double WordProgress
    {
        get => _wordProgress;
        set => SetProperty(ref _wordProgress, Math.Clamp(value, 0, 1));
    }

    public int DistanceFromCurrent
    {
        get => _distanceFromCurrent;
        set
        {
            if (SetProperty(ref _distanceFromCurrent, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(TextOpacity));
            }
        }
    }

    public bool IsManualBrowsing
    {
        get => _isManualBrowsing;
        set
        {
            if (SetProperty(ref _isManualBrowsing, value))
            {
                OnPropertyChanged(nameof(TextOpacity));
            }
        }
    }

    public double TextOpacity => IsManualBrowsing
        ? 1
        : IsCurrent
        ? 1
        : DistanceFromCurrent switch
        {
            <= 1 => 0.66,
            _ => 0.44
        };
    public double TextSize => IsCurrent ? 22 : 16;
}
