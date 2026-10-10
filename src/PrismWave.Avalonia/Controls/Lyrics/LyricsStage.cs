using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using PrismWave_WinUI.Infrastructure.Lyrics;
using PrismWave_WinUI.Models;

namespace PrismWave.Avalonia.Controls.Lyrics;

public sealed class LyricsLineInvokedEventArgs(int lineIndex) : EventArgs
{
    public int LineIndex { get; } = lineIndex;
}

public sealed class LyricsManualBrowseChangedEventArgs(bool isManualBrowsing) : EventArgs
{
    public bool IsManualBrowsing { get; } = isManualBrowsing;
}

/// <summary>
/// 歌词舞台：Avalonia 自绘（对齐 WinUI LyricsStageControl 的场景行为）。
/// 复用 Core 的 LyricsSceneController 场景状态机；非活动行用低透明度近似高斯模糊。
/// </summary>
public sealed class LyricsStage : Control
{
    private const double HorizontalPadding = 28d;
    private const double LineGap = 28d;
    private const double MinimumLineHeight = 68d;
    private const double DragThreshold = 5d;
    private const double FrameIntervalMilliseconds = 16;

    private static readonly Typeface LineTypeface =
        new("Segoe UI Variable Display,Segoe UI", FontStyle.Normal, FontWeight.SemiBold);

    private static readonly IBrush BaseBrush = new SolidColorBrush(Color.FromRgb(136, 136, 136));
    private static readonly IBrush HighlightBrush = Brushes.White;

    private readonly LyricsSceneController _scene = new();
    private readonly Stopwatch _clock = new();
    private readonly List<LyricsLayoutEntry> _layoutCache = [];
    private readonly DispatcherTimer _renderTimer;
    private IReadOnlyList<LyricLineModel> _lyrics = Array.Empty<LyricLineModel>();
    private double _layoutWidth = -1;
    private bool _isDragging;
    private bool _dragExceededThreshold;
    private Point _pointerPressedAt;
    private Point _lastPointerPosition;

    public LyricsStage()
    {
        Focusable = false;
        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(FrameIntervalMilliseconds)
        };
        _renderTimer.Tick += RenderTimer_Tick;
        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        LayoutUpdated += (_, _) => RebuildLayoutCache();
        DetachedFromVisualTree += (_, _) => StopRenderLoop();
    }

    public event EventHandler<LyricsLineInvokedEventArgs>? LyricInvoked;
    public event EventHandler<LyricsManualBrowseChangedEventArgs>? ManualBrowseChanged;

    public bool IsManualBrowsing => _scene.IsManualBrowsing;

    public void SetLyrics(IReadOnlyList<LyricLineModel> lyrics, int revision)
    {
        _lyrics = lyrics ?? Array.Empty<LyricLineModel>();
        _scene.SetLyrics(_lyrics, revision);
        // 先清缓存，避免渲染帧按旧行数访问新 bounds 数组越界
        DisposeLayoutCache();
        RebuildLayoutCache();
        InvalidateVisual();
    }

    public void UpdatePlaybackSample(
        double positionSeconds,
        bool isPlaying,
        LyricsPositionUpdateKind updateKind)
    {
        EnsureClockStarted();
        _scene.UpdatePlaybackSample(
            positionSeconds,
            isPlaying,
            updateKind,
            _clock.Elapsed.TotalSeconds,
            Bounds.Height);
        EnsureRenderLoop();
        InvalidateVisual();
    }

    public void SetManualBrowsing(bool isManualBrowsing)
    {
        if (isManualBrowsing)
        {
            BeginManualBrowse();
        }
        else
        {
            EndManualBrowse();
        }
    }

    public void EndManualBrowse()
    {
        if (!_scene.IsManualBrowsing)
        {
            return;
        }

        EnsureClockStarted();
        _scene.EndManualBrowse(_clock.Elapsed.TotalSeconds, Bounds.Height);
        ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(false));
        EnsureRenderLoop();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        if (_layoutCache.Count == 0)
        {
            return;
        }

        var viewportWidth = Math.Max(1, Bounds.Width);
        var viewportHeight = Math.Max(1, Bounds.Height);
        for (var index = 0; index < _layoutCache.Count; index++)
        {
            if (index >= _scene.LineCount)
            {
                break;
            }

            var bounds = _scene.GetLineBounds(index);
            var centerY = (viewportHeight / 2) + (bounds.Center - _scene.ScrollOffset);
            var visual = _scene.GetLineVisualState(index);
            var visibleHalfHeight = (bounds.Height * visual.Scale / 2) + 32;
            if (centerY + visibleHalfHeight < 0 || centerY - visibleHalfHeight > viewportHeight)
            {
                continue;
            }

            DrawLine(context, _layoutCache[index], visual, viewportWidth, centerY);
        }
    }

    private void DrawLine(
        DrawingContext context,
        LyricsLayoutEntry entry,
        LyricsLineVisualState visual,
        double viewportWidth,
        double centerY)
    {
        var origin = new Point(HorizontalPadding, centerY - (entry.Height / 2));
        using var transformPush = context.PushTransform(
            Matrix.CreateTranslation(viewportWidth / 2, centerY)
            * Matrix.CreateScale(visual.Scale, visual.Scale)
            * Matrix.CreateTranslation(-viewportWidth / 2, -centerY));
        using var opacityPush = context.PushOpacity(visual.Opacity);
        context.DrawText(entry.Base, origin);
        if (visual.KaraokeProgress > 0 && visual.Activation > 0.001)
        {
            var progress = Math.Clamp(visual.KaraokeProgress, 0, 1);
            var textLeft = HorizontalPadding + ((entry.LayoutWidth - entry.Width) / 2);
            var revealWidth = Math.Max(0, entry.Width * progress);
            using var clipPush = context.PushClip(
                new Rect(textLeft, origin.Y, revealWidth, entry.Height + 2));
            context.DrawText(entry.Highlight, origin);
        }
    }

    private void RebuildLayoutCache()
    {
        var layoutWidth = Math.Max(1, Bounds.Width - (HorizontalPadding * 2));
        if (Bounds.Width <= 0)
        {
            return;
        }

        if (_layoutCache.Count > 0 && Math.Abs(layoutWidth - _layoutWidth) < 0.5)
        {
            return;
        }

        _layoutWidth = layoutWidth;
        DisposeLayoutCache();
        var formatSize = LyricsSceneController.CurrentFontSize;
        for (var index = 0; index < _lyrics.Count; index++)
        {
            var text = _lyrics[index].Text ?? string.Empty;
            var baseText = CreateFormattedText(text, layoutWidth, BaseBrush);
            var highlightText = CreateFormattedText(text, layoutWidth, HighlightBrush);
            var contentHeight = Math.Max(baseText.Height, highlightText.Height);
            var height = Math.Max(MinimumLineHeight, contentHeight + 16);
            _layoutCache.Add(new LyricsLayoutEntry(
                text,
                baseText,
                highlightText,
                layoutWidth,
                height));
        }

        _scene.SetLineMetrics(_layoutCache.Select(entry => entry.Height).ToArray(), LineGap);
        InvalidateVisual();
    }

    private static FormattedText CreateFormattedText(string text, double layoutWidth, IBrush brush)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            LineTypeface,
            LyricsSceneController.CurrentFontSize,
            brush);
        formatted.MaxTextWidth = layoutWidth;
        formatted.TextAlignment = TextAlignment.Center;
        return formatted;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta == 0)
        {
            return;
        }

        _scene.ScrollBy(-delta * 38, Bounds.Height);
        NotifyManualBrowseInteraction();
        EnsureRenderLoop();
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointerPressedAt = point.Position;
        _lastPointerPosition = point.Position;
        _dragExceededThreshold = false;
        e.Pointer.Capture(this);
        _isDragging = true;
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (!_dragExceededThreshold && Distance(position, _pointerPressedAt) >= DragThreshold)
        {
            _dragExceededThreshold = true;
        }

        if (_dragExceededThreshold)
        {
            _scene.ScrollBy(_lastPointerPosition.Y - position.Y, Bounds.Height);
            NotifyManualBrowseInteraction();
            InvalidateVisual();
        }

        _lastPointerPosition = position;
        e.Handled = _dragExceededThreshold;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        var wasDrag = _dragExceededThreshold;
        _isDragging = false;
        _dragExceededThreshold = false;
        e.Pointer.Capture(null);
        if (!wasDrag)
        {
            var position = e.GetPosition(this);
            var lineIndex = _scene.HitTest(position.Y, Bounds.Height);
            if (lineIndex >= 0)
            {
                LyricInvoked?.Invoke(this, new LyricsLineInvokedEventArgs(lineIndex));
            }
        }
        else if (_scene.IsManualBrowsing)
        {
            NotifyManualBrowseInteraction();
        }

        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isDragging = false;
        _dragExceededThreshold = false;
    }

    private void BeginManualBrowse()
    {
        _scene.BeginManualBrowse();
        NotifyManualBrowseInteraction();
        InvalidateVisual();
    }

    private void NotifyManualBrowseInteraction() =>
        ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(true));

    private void EnsureRenderLoop()
    {
        if (_renderTimer.IsEnabled || !_scene.NeedsFrames)
        {
            return;
        }

        _renderTimer.Start();
    }

    private void StopRenderLoop()
    {
        _renderTimer.Stop();
    }

    private void RenderTimer_Tick(object? sender, EventArgs e)
    {
        _scene.Advance(_clock.Elapsed.TotalSeconds, Bounds.Height);
        InvalidateVisual();
        if (!_scene.NeedsFrames)
        {
            StopRenderLoop();
        }
    }

    private void EnsureClockStarted()
    {
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    private void DisposeLayoutCache()
    {
        _layoutCache.Clear();
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private sealed class LyricsLayoutEntry(
        string text,
        FormattedText baseText,
        FormattedText highlightText,
        double layoutWidth,
        double height)
    {
        public string Text { get; } = text;
        public FormattedText Base { get; } = baseText;
        public FormattedText Highlight { get; } = highlightText;
        public double LayoutWidth { get; } = layoutWidth;
        public double Height { get; } = height;
        public double Width => Math.Max(Base.Width, Highlight.Width);
    }
}
