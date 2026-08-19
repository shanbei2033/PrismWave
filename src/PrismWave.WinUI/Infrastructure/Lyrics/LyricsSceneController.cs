using PrismWave_WinUI.Models;

namespace PrismWave_WinUI.Infrastructure.Lyrics;

public enum LyricsPositionUpdateKind
{
    Sample,
    Seek,
    TrackChanged,
    PauseResume,
    OffsetChanged
}

public readonly record struct LyricsLineBounds(double Top, double Height)
{
    public double Center => Top + (Height / 2);
    public double Bottom => Top + Height;
}

public readonly record struct LyricsLineVisualState(
    double Scale,
    double Opacity,
    double BlurAmount,
    double Activation,
    double KaraokeProgress);

public readonly record struct LyricsSceneFrame(
    double PresentationPositionSeconds,
    int ActiveIndex,
    int PreviousActiveIndex,
    double ScrollOffset,
    double TransitionProgress,
    bool IsTransitioning);

public sealed class LyricsSceneController
{
    public const double NaturalTransitionSeconds = 0.320;
    public const double RapidTransitionSeconds = 0.180;
    public const double CurrentFontSize = 34;
    public const double InactiveFontScale = 24d / CurrentFontSize;

    private IReadOnlyList<LyricLineModel> _lines = Array.Empty<LyricLineModel>();
    private LyricsLineBounds[] _lineBounds = Array.Empty<LyricsLineBounds>();
    private LyricsLineVisualState[] _transitionStarts = Array.Empty<LyricsLineVisualState>();
    private int _documentRevision;
    private bool _hasPosition;
    private bool _isPlaying;
    private bool _isManualBrowsing;
    private double _anchorPositionSeconds;
    private double _anchorClockSeconds;
    private double _presentationPositionSeconds;
    private int _activeIndex = -1;
    private int _previousActiveIndex = -1;
    private double _scrollOffset;
    private double _scrollStart;
    private double _scrollTarget;
    private double _transitionStartedAt;
    private double _transitionDuration;
    private double _transitionProgress = 1;
    private bool _isTransitioning;
    private bool _scrollWasRebased;
    private double _outgoingProgress;
    private double _activeKaraokeProgress;

    public int DocumentRevision => _documentRevision;
    public int ActiveIndex => _activeIndex;
    public int PreviousActiveIndex => _previousActiveIndex;
    public double PresentationPositionSeconds => _presentationPositionSeconds;
    public double ScrollOffset => _scrollOffset;
    public bool IsManualBrowsing => _isManualBrowsing;
    public bool NeedsFrames => _isPlaying || _isTransitioning;
    public int LineCount => _lines.Count;

    public void SetLyrics(IReadOnlyList<LyricLineModel> lines, int revision)
    {
        _lines = lines ?? Array.Empty<LyricLineModel>();
        _documentRevision = revision;
        _lineBounds = BuildBounds(Enumerable.Repeat(68d, _lines.Count).ToArray(), 28);
        _transitionStarts = new LyricsLineVisualState[_lines.Count];
        _hasPosition = false;
        _activeIndex = -1;
        _previousActiveIndex = -1;
        _presentationPositionSeconds = 0;
        _anchorPositionSeconds = 0;
        _anchorClockSeconds = 0;
        _scrollOffset = _lines.Count == 0 ? 0 : _lineBounds[0].Center;
        _scrollStart = _scrollOffset;
        _scrollTarget = _scrollOffset;
        _transitionProgress = 1;
        _isTransitioning = false;
        _isManualBrowsing = false;
        _outgoingProgress = 0;
        _activeKaraokeProgress = 0;
    }

    public void SetLineMetrics(IReadOnlyList<double> heights, double gap)
    {
        ArgumentNullException.ThrowIfNull(heights);
        if (heights.Count != _lines.Count)
        {
            throw new ArgumentException("A height is required for every lyric line.", nameof(heights));
        }

        _lineBounds = BuildBounds(heights, gap);
        if (_activeIndex >= 0)
        {
            _scrollOffset = GetLineCenter(_activeIndex);
            _scrollStart = _scrollOffset;
            _scrollTarget = _scrollOffset;
        }
    }

    public LyricsSceneFrame UpdatePlaybackSample(
        double positionSeconds,
        bool isPlaying,
        LyricsPositionUpdateKind updateKind,
        double clockSeconds,
        double viewportHeight)
    {
        var clock = NormalizeClock(clockSeconds);
        var sample = NormalizePosition(positionSeconds);
        var isForced = !_hasPosition || updateKind != LyricsPositionUpdateKind.Sample || !isPlaying;

        if (isForced)
        {
            _presentationPositionSeconds = sample;
        }
        else
        {
            AdvancePosition(clock);
            _presentationPositionSeconds = Math.Max(_presentationPositionSeconds, sample);
        }

        _hasPosition = true;
        _isPlaying = isPlaying;
        _anchorPositionSeconds = _presentationPositionSeconds;
        _anchorClockSeconds = clock;
        UpdateActiveLine(clock, viewportHeight, updateKind);
        return Advance(clock, viewportHeight);
    }

    public LyricsSceneFrame Advance(double clockSeconds, double viewportHeight)
    {
        var clock = NormalizeClock(clockSeconds);
        AdvancePosition(clock);
        UpdateActiveLine(clock, viewportHeight, LyricsPositionUpdateKind.Sample);
        AdvanceTransition(clock);
        return CreateFrame();
    }

    public void BeginManualBrowse()
    {
        _isManualBrowsing = true;
        _isTransitioning = false;
        _transitionProgress = 1;
        _previousActiveIndex = -1;
    }

    public void ScrollBy(double delta, double viewportHeight)
    {
        if (_lines.Count == 0 || !double.IsFinite(delta))
        {
            return;
        }

        BeginManualBrowse();
        var minimum = GetLineCenter(0);
        var maximum = GetLineCenter(_lines.Count - 1);
        _scrollOffset = Math.Clamp(_scrollOffset + delta, minimum, maximum);
        _scrollStart = _scrollOffset;
        _scrollTarget = _scrollOffset;
    }

    public void EndManualBrowse(double clockSeconds, double viewportHeight)
    {
        if (!_isManualBrowsing)
        {
            return;
        }

        _isManualBrowsing = false;
        StartTransition(
            _activeIndex,
            _activeIndex,
            NormalizeClock(clockSeconds),
            LyricsPositionUpdateKind.Seek,
            viewportHeight);
    }

    public int HitTest(double viewportY, double viewportHeight)
    {
        if (_lineBounds.Length == 0 || !double.IsFinite(viewportY) || !double.IsFinite(viewportHeight))
        {
            return -1;
        }

        var sceneY = _scrollOffset + viewportY - (Math.Max(0, viewportHeight) / 2);
        for (var index = 0; index < _lineBounds.Length; index++)
        {
            var bounds = _lineBounds[index];
            if (sceneY >= bounds.Top && sceneY <= bounds.Bottom)
            {
                return index;
            }
        }

        return -1;
    }

    public LyricsLineBounds GetLineBounds(int index) => _lineBounds[index];

    public double GetLineCenter(int index) => _lineBounds[index].Center;

    public LyricsLineVisualState GetLineVisualState(int index)
    {
        if (index < 0 || index >= _lines.Count)
        {
            return default;
        }

        var target = ResolveTargetVisual(index);
        var visual = _isTransitioning && index < _transitionStarts.Length
            ? Interpolate(_transitionStarts[index], target, EaseInOutCubic(_transitionProgress))
            : target;
        double karaokeProgress;
        if (index == _activeIndex)
        {
            karaokeProgress = CalculateKaraokeProgress(index, _presentationPositionSeconds);
            // 位置采样抖动防御：同一行内逐字进度只进不退，
            // 避免强制位置更新（暂停/偏移/采样竞态）导致点亮倒退闪烁。
            if (karaokeProgress + 0.001 < _activeKaraokeProgress)
            {
                karaokeProgress = _activeKaraokeProgress;
            }

            _activeKaraokeProgress = karaokeProgress;
        }
        else if (index == _previousActiveIndex && _isTransitioning)
        {
            karaokeProgress = _outgoingProgress;
        }
        else
        {
            karaokeProgress = 0;
        }

        return visual with { KaraokeProgress = karaokeProgress };
    }

    private static LyricsLineBounds[] BuildBounds(IReadOnlyList<double> heights, double gap)
    {
        var normalizedGap = double.IsFinite(gap) ? Math.Max(0, gap) : 0;
        var bounds = new LyricsLineBounds[heights.Count];
        var top = 0d;
        for (var index = 0; index < heights.Count; index++)
        {
            var height = double.IsFinite(heights[index]) ? Math.Max(1, heights[index]) : 68;
            bounds[index] = new LyricsLineBounds(top, height);
            top += height + normalizedGap;
        }

        return bounds;
    }

    private void AdvancePosition(double clockSeconds)
    {
        if (!_hasPosition || !_isPlaying)
        {
            return;
        }

        var elapsed = Math.Max(0, clockSeconds - _anchorClockSeconds);
        _presentationPositionSeconds = Math.Max(
            _presentationPositionSeconds,
            _anchorPositionSeconds + elapsed);
    }

    private void UpdateActiveLine(
        double clockSeconds,
        double viewportHeight,
        LyricsPositionUpdateKind updateKind)
    {
        var nextIndex = FindActiveIndex(_presentationPositionSeconds);
        if (nextIndex == _activeIndex)
        {
            return;
        }

        var previousIndex = _activeIndex;
        if (previousIndex < 0 || updateKind == LyricsPositionUpdateKind.TrackChanged)
        {
            _activeIndex = nextIndex;
            _previousActiveIndex = -1;
            _activeKaraokeProgress = 0;
            _scrollTarget = nextIndex < 0 ? 0 : GetLineCenter(nextIndex);
            _scrollOffset = _scrollTarget;
            _scrollStart = _scrollTarget;
            _transitionProgress = 1;
            _isTransitioning = false;
            return;
        }

        StartTransition(previousIndex, nextIndex, clockSeconds, updateKind, viewportHeight);
    }

    private void StartTransition(
        int previousIndex,
        int nextIndex,
        double clockSeconds,
        LyricsPositionUpdateKind updateKind,
        double viewportHeight)
    {
        AdvanceTransition(clockSeconds);
        EnsureTransitionBuffer();
        for (var index = 0; index < _lines.Count; index++)
        {
            _transitionStarts[index] = GetLineVisualState(index);
        }

        if (previousIndex >= 0)
        {
            _outgoingProgress = CalculateKaraokeProgress(previousIndex, _presentationPositionSeconds);
        }

        _previousActiveIndex = previousIndex;
        _activeIndex = nextIndex;
        // 快照循环会以旧行为 active 更新钳制值，必须在 index 切换后重置，
        // 否则新行逐字进度被旧行终值（≈1）污染而直接全白。
        _activeKaraokeProgress = 0;
        _scrollStart = _scrollOffset;
        _scrollTarget = nextIndex < 0 ? _scrollOffset : GetLineCenter(nextIndex);
        var isNatural = updateKind == LyricsPositionUpdateKind.Sample;
        _transitionDuration = isNatural ? NaturalTransitionSeconds : RapidTransitionSeconds;
        _transitionStartedAt = clockSeconds;
        _transitionProgress = 0;
        _isTransitioning = _transitionDuration > 0;
        _scrollWasRebased = !isNatural && previousIndex >= 0 && nextIndex >= 0
            && Math.Abs(nextIndex - previousIndex) > 3;
        if (_scrollWasRebased)
        {
            _scrollOffset = _scrollTarget;
            _scrollStart = _scrollTarget;
        }
    }

    private void AdvanceTransition(double clockSeconds)
    {
        if (!_isTransitioning)
        {
            return;
        }

        _transitionProgress = Math.Clamp(
            (clockSeconds - _transitionStartedAt) / Math.Max(0.001, _transitionDuration),
            0,
            1);
        if (!_isManualBrowsing && !_scrollWasRebased)
        {
            var eased = EaseInOutCubic(_transitionProgress);
            _scrollOffset = Lerp(_scrollStart, _scrollTarget, eased);
        }

        if (_transitionProgress < 1)
        {
            return;
        }

        _scrollOffset = _scrollTarget;
        _isTransitioning = false;
        _previousActiveIndex = -1;
        _outgoingProgress = 0;
    }

    private LyricsSceneFrame CreateFrame() => new(
        _presentationPositionSeconds,
        _activeIndex,
        _previousActiveIndex,
        _scrollOffset,
        _transitionProgress,
        _isTransitioning);

    private int FindActiveIndex(double positionSeconds)
    {
        if (_lines.Count == 0)
        {
            return -1;
        }

        var low = 0;
        var high = _lines.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (_lines[middle].TimeSeconds <= positionSeconds)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return Math.Clamp(high, 0, _lines.Count - 1);
    }

    private LyricsLineVisualState ResolveTargetVisual(int index)
    {
        var isCurrent = index == _activeIndex;
        if (isCurrent)
        {
            return new LyricsLineVisualState(1, 1, 0, 1, 0);
        }

        var distance = _activeIndex < 0 ? int.MaxValue : Math.Abs(index - _activeIndex);
        var isNear = distance <= 1;
        return new LyricsLineVisualState(
            InactiveFontScale * (isNear ? 0.98 : 0.97),
            _isManualBrowsing ? 1 : isNear ? 0.66 : 0.36,
            _isManualBrowsing ? 0 : isNear ? 1 : 1.9,
            0,
            0);
    }

    private double CalculateKaraokeProgress(int lineIndex, double positionSeconds)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count)
        {
            return 0;
        }

        var line = _lines[lineIndex];
        if (line.TimedSegments.Count > 0)
        {
            var totalCharacters = line.TimedSegments.Sum(segment => CountPaintableCharacters(segment.Text));
            if (totalCharacters <= 0)
            {
                return 0;
            }

            var completed = 0d;
            foreach (var segment in line.TimedSegments)
            {
                var length = CountPaintableCharacters(segment.Text);
                if (positionSeconds >= segment.EndSeconds)
                {
                    completed += length;
                    continue;
                }

                if (positionSeconds > segment.StartSeconds)
                {
                    completed += length * Math.Clamp(
                        (positionSeconds - segment.StartSeconds) /
                        Math.Max(0.001, segment.EndSeconds - segment.StartSeconds),
                        0,
                        1);
                }

                break;
            }

            return Math.Clamp(completed / totalCharacters, 0, 1);
        }

        var start = line.TimeSeconds;
        var end = lineIndex + 1 < _lines.Count ? _lines[lineIndex + 1].TimeSeconds : start + 3;
        var duration = end - start;
        if (duration <= 0)
        {
            return positionSeconds >= start ? 1 : 0;
        }

        var raw = Math.Clamp((positionSeconds - start) / duration, 0, 1);
        return raw * raw * (3 - (2 * raw));
    }

    private void EnsureTransitionBuffer()
    {
        if (_transitionStarts.Length != _lines.Count)
        {
            _transitionStarts = new LyricsLineVisualState[_lines.Count];
        }
    }

    private static LyricsLineVisualState Interpolate(
        LyricsLineVisualState from,
        LyricsLineVisualState to,
        double progress) => new(
        Lerp(from.Scale, to.Scale, progress),
        Lerp(from.Opacity, to.Opacity, progress),
        Lerp(from.BlurAmount, to.BlurAmount, progress),
        Lerp(from.Activation, to.Activation, progress),
        Lerp(from.KaraokeProgress, to.KaraokeProgress, progress));

    private static double EaseInOutCubic(double progress)
    {
        var value = Math.Clamp(progress, 0, 1);
        return value < 0.5
            ? 4 * value * value * value
            : 1 - (Math.Pow(-2 * value + 2, 3) / 2);
    }

    private static double Lerp(double from, double to, double progress) =>
        from + ((to - from) * Math.Clamp(progress, 0, 1));

    private static int CountPaintableCharacters(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Count(character => !char.IsWhiteSpace(character));

    private static double NormalizePosition(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;

    private static double NormalizeClock(double value) =>
        double.IsFinite(value) ? Math.Max(0, value) : 0;
}
