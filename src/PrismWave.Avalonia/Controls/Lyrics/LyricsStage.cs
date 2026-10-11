using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
/// 复用 Core 的 LyricsSceneController 场景状态机；每行为一个 LyricsLinePresenter 子视觉，
/// 非活动行通过预渲染模糊位图交叉淡化实现模糊（Avalonia 11.3 无内建 BlurEffect），
/// 模糊度/透明度/缩放随状态机插值平滑衔接。
/// 帧循环用 TopLevel.RequestAnimationFrame 对齐显示器刷新率。
/// </summary>
public sealed class LyricsStage : Panel
{
    private const double HorizontalPadding = 28d;
    private const double LineGap = 14d;
    private const double MinimumLineHeight = 72d;
    private const double DragThreshold = 5d;

    private static readonly Typeface LineTypeface =
        new("Segoe UI Variable Display,Segoe UI", FontStyle.Normal, FontWeight.Bold);

    private static readonly IBrush BaseBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0xA8, 0xB0));
    private static readonly IBrush HighlightBrush = Brushes.White;

    private readonly LyricsSceneController _scene = new();
    private readonly Stopwatch _clock = new();
    private readonly List<LyricsLayoutEntry> _layoutCache = [];
    private readonly List<LyricsLinePresenter> _presenters = [];
    private IReadOnlyList<LyricLineModel> _lyrics = Array.Empty<LyricLineModel>();
    private double _layoutWidth = -1;
    private bool _isDragging;
    private bool _dragExceededThreshold;
    private bool _frameScheduled;
    private Point _pointerPressedAt;
    private Point _lastPointerPosition;

    public LyricsStage()
    {
        Focusable = false;
        PointerWheelChanged += OnPointerWheelChanged;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        LayoutUpdated += (_, _) => RebuildLayoutCache();
        DetachedFromVisualTree += (_, _) => _frameScheduled = false;
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
        UpdateVisuals();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 0;
        var height = double.IsFinite(availableSize.Height) ? availableSize.Height : 0;
        var childConstraint = new Size(Math.Max(0, width - (HorizontalPadding * 2)), height);
        foreach (var child in Children)
        {
            child.Measure(childConstraint);
        }

        return new Size(width, height);
    }

    /// <summary>行 Presenter 统一排布在左内边距处（缩放关于本地 (0,0)，左缘对齐不随 scale 漂移）。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var childRect = new Rect(
            HorizontalPadding,
            0,
            Math.Max(0, finalSize.Width - (HorizontalPadding * 2)),
            finalSize.Height);
        foreach (var child in Children)
        {
            child.Arrange(childRect);
        }

        return finalSize;
    }

    /// <summary>逐帧把场景状态应用到行 Presenter（定位/缩放/透明度/模糊/卡拉OK），替代整体自绘。</summary>
    private void UpdateVisuals()
    {
        if (_presenters.Count == 0)
        {
            return;
        }

        var viewportHeight = Math.Max(1, Bounds.Height);
        for (var index = 0; index < _presenters.Count; index++)
        {
            if (index >= _scene.LineCount)
            {
                _presenters[index].IsVisible = false;
                continue;
            }

            var presenter = _presenters[index];
            var bounds = _scene.GetLineBounds(index);
            var centerY = (viewportHeight / 2) + (bounds.Center - _scene.ScrollOffset);
            var visual = _scene.GetLineVisualState(index);
            var visibleHalfHeight = (bounds.Height * visual.Scale / 2) + 32;
            if (centerY + visibleHalfHeight < 0 || centerY - visibleHalfHeight > viewportHeight)
            {
                presenter.IsVisible = false;
                continue;
            }

            presenter.IsVisible = true;
            presenter.ApplyState(
                centerY - (bounds.Height / 2),
                visual.Scale,
                visual.Opacity,
                visual.BlurAmount,
                visual.KaraokeProgress,
                visual.Activation);
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
        SyncPresenters();
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
            _presenters[index].SetContent(baseText, highlightText, height);
        }

        _scene.SetLineMetrics(_layoutCache.Select(entry => entry.Height).ToArray(), LineGap);
        UpdateVisuals();
    }

    private void SyncPresenters()
    {
        foreach (var extra in _presenters.Skip(_lyrics.Count))
        {
            Children.Remove(extra);
        }

        if (_presenters.Count > _lyrics.Count)
        {
            _presenters.RemoveRange(_lyrics.Count, _presenters.Count - _lyrics.Count);
        }

        while (_presenters.Count < _lyrics.Count)
        {
            var presenter = new LyricsLinePresenter();
            _presenters.Add(presenter);
            Children.Add(presenter);
        }
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
        formatted.TextAlignment = TextAlignment.Left;
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
        UpdateVisuals();
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
            UpdateVisuals();
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
        UpdateVisuals();
    }

    private void NotifyManualBrowseInteraction() =>
        ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(true));

    /// <summary>用 RequestAnimationFrame 驱动帧循环，对齐显示器刷新率（替代 16ms Timer 的 60fps 上限）。</summary>
    private void EnsureRenderLoop()
    {
        if (_frameScheduled || !_scene.NeedsFrames)
        {
            UpdateVisuals();
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
        {
            UpdateVisuals();
            return;
        }

        _frameScheduled = true;
        topLevel.RequestAnimationFrame(OnAnimationFrame);
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        _frameScheduled = false;
        _scene.Advance(_clock.Elapsed.TotalSeconds, Bounds.Height);
        UpdateVisuals();
        if (_scene.NeedsFrames)
        {
            EnsureRenderLoop();
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
