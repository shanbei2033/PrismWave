using System.Diagnostics;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PrismWave_WinUI.Infrastructure.Lyrics;
using PrismWave_WinUI.Models;
using Windows.Foundation;
using Windows.UI;

namespace PrismWave_WinUI.Controls.Lyrics;

public sealed class LyricsLineInvokedEventArgs(int lineIndex) : EventArgs
{
    public int LineIndex { get; } = lineIndex;
}

public sealed class LyricsManualBrowseChangedEventArgs(bool isManualBrowsing) : EventArgs
{
    public bool IsManualBrowsing { get; } = isManualBrowsing;
}

public sealed partial class LyricsStageControl : UserControl
{
    private const float HorizontalPadding = 28f;
    private const float LineGap = 28f;
    private const float MinimumLineHeight = 68f;
    private const float CompanionLineGap = 6f;
    private const float BlurPaddingMultiplier = 3f;
    private const float CrispOverlayOpacity = 0.18f;
    private const float DragThreshold = 5f;
    private static readonly Color CompanionTextColor = Color.FromArgb(255, 150, 150, 150);

    private readonly LyricsSceneController _scene = new();
    private readonly Stopwatch _clock = new();
    private readonly List<LyricsLayoutEntry> _layoutCache = [];
    private IReadOnlyList<LyricLineModel> _lyrics = Array.Empty<LyricLineModel>();
    private double _lastPositionSeconds;
    private CanvasSolidColorBrush? _baseBrush;
    private CanvasSolidColorBrush? _highlightBrush;
    private CanvasSolidColorBrush? _partialBrush;
    private CanvasSolidColorBrush? _companionHighlightBrush;
    private bool _resourcesReady;
    private bool _renderingSubscribed;
    private bool _isDragging;
    private bool _dragExceededThreshold;
    private uint _capturedPointerId;
    private Point _pointerPressedAt;
    private Point _lastPointerPosition;
    private int _lastAccessibleLine = -1;

    public LyricsStageControl()
    {
        InitializeComponent();
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        Loaded += LyricsStageControl_Loaded;
        Unloaded += LyricsStageControl_Unloaded;
    }

    public event EventHandler<LyricsLineInvokedEventArgs>? LyricInvoked;
    public event EventHandler<LyricsManualBrowseChangedEventArgs>? ManualBrowseChanged;

    public bool IsManualBrowsing => _scene.IsManualBrowsing;

    internal string AccessibleLyricText =>
        _scene.ActiveIndex >= 0 && _scene.ActiveIndex < _lyrics.Count
            ? _lyrics[_scene.ActiveIndex].Text
            : string.Empty;

    public void SetLyrics(IReadOnlyList<LyricLineModel> lyrics, int revision)
    {
        _lyrics = lyrics ?? Array.Empty<LyricLineModel>();
        _scene.SetLyrics(_lyrics, revision);
        RebuildLayoutCache();
        UpdateAccessibleLine();
        StageCanvas.Invalidate();
    }

    public void UpdatePlaybackSample(
        double positionSeconds,
        bool isPlaying,
        LyricsPositionUpdateKind updateKind)
    {
        EnsureClockStarted();
        _lastPositionSeconds = positionSeconds;
        _scene.UpdatePlaybackSample(
            positionSeconds,
            isPlaying,
            updateKind,
            _clock.Elapsed.TotalSeconds,
            ActualHeight);
        UpdateAccessibleLine();
        EnsureRenderingSubscription();
        StageCanvas.Invalidate();
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
        _scene.EndManualBrowse(_clock.Elapsed.TotalSeconds, ActualHeight);
        ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(false));
        EnsureRenderingSubscription();
        StageCanvas.Invalidate();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LyricsStageAutomationPeer(this);

    private void LyricsStageControl_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureClockStarted();
        if (StageCanvas.ReadyToDraw)
        {
            EnsureDeviceResources(StageCanvas);
            RebuildLayoutCache();
        }

        EnsureRenderingSubscription();
        StageCanvas.Invalidate();
    }

    private void LyricsStageControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopRenderingSubscription();
        _clock.Reset();
        _isDragging = false;
        _capturedPointerId = 0;
        DisposeLayoutCache();
        DisposeBrushes();
        _resourcesReady = false;
    }

    private void StageCanvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        DisposeBrushes();
        EnsureDeviceResources(sender);
        RebuildLayoutCache();
    }

    private void EnsureDeviceResources(CanvasControl resourceCreator)
    {
        if (_baseBrush is not null && _highlightBrush is not null && _partialBrush is not null
            && _companionHighlightBrush is not null)
        {
            _resourcesReady = true;
            return;
        }

        DisposeBrushes();
        _baseBrush = new CanvasSolidColorBrush(resourceCreator, Color.FromArgb(255, 136, 136, 136));
        _highlightBrush = new CanvasSolidColorBrush(resourceCreator, Microsoft.UI.Colors.White);
        _partialBrush = new CanvasSolidColorBrush(resourceCreator, Microsoft.UI.Colors.White);
        // 副行高亮画刷与主行独立：部分亮插值进度不同步，共享实例会互相污染；
        // 副行间的部分亮差异由每个 CompanionEntry 自带的 PartialBrush 承载。
        _companionHighlightBrush = new CanvasSolidColorBrush(resourceCreator, Microsoft.UI.Colors.White);
        _resourcesReady = true;
    }

    private void StageCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 0.5)
        {
            RebuildLayoutCache();
        }

        StageCanvas.Invalidate();
    }

    private void CompositionTarget_Rendering(object? sender, object e)
    {
        if (!IsLoaded)
        {
            StopRenderingSubscription();
            return;
        }

        _scene.Advance(_clock.Elapsed.TotalSeconds, ActualHeight);
        UpdateAccessibleLine();
        StageCanvas.Invalidate();
        if (!_scene.NeedsFrames)
        {
            StopRenderingSubscription();
        }
    }

    private void StageCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (!_resourcesReady || _layoutCache.Count == 0)
        {
            return;
        }

        var viewportWidth = Math.Max(1, (float)sender.ActualWidth);
        var viewportHeight = Math.Max(1, (float)sender.ActualHeight);
        var drawingSession = args.DrawingSession;
        for (var index = 0; index < _layoutCache.Count; index++)
        {
            var bounds = _scene.GetLineBounds(index);
            var centerY = (viewportHeight / 2) + (float)(bounds.Center - _scene.ScrollOffset);
            var visual = _scene.GetLineVisualState(index);
            var visibleHalfHeight = (float)(bounds.Height * visual.Scale / 2) + 32;
            if (centerY + visibleHalfHeight < 0 || centerY - visibleHalfHeight > viewportHeight)
            {
                continue;
            }

            DrawLine(
                sender,
                drawingSession,
                _layoutCache[index],
                visual,
                viewportWidth,
                centerY);
        }
    }

    private void DrawLine(
        CanvasControl sender,
        CanvasDrawingSession drawingSession,
        LyricsLayoutEntry entry,
        LyricsLineVisualState visual,
        float viewportWidth,
        float centerY)
    {
        if (_baseBrush is null || _highlightBrush is null || _partialBrush is null)
        {
            return;
        }

        ResetAndApplyKaraokeBrushes(entry, visual);
        ApplyCompanionKaraokeBrushes(entry, visual);
        var x = HorizontalPadding;
        var y = centerY - ((float)entry.Height / 2);
        var originalTransform = drawingSession.Transform;
        drawingSession.Transform = Matrix3x2.CreateScale(
            (float)visual.Scale,
            new Vector2(viewportWidth / 2, centerY)) * originalTransform;
        try
        {
            using var opacityLayer = drawingSession.CreateLayer((float)visual.Opacity);
            if (visual.BlurAmount <= 0.01)
            {
                drawingSession.DrawTextLayout(entry.Layout, x, y, Color.FromArgb(255, 136, 136, 136));
                DrawCompanionLayouts(drawingSession, entry, x, y);
                return;
            }

            CanvasCommandList? dynamicCommandList = null;
            var effectSource = entry.StaticCommandList;
            if (visual.KaraokeProgress > 0 || visual.Activation > 0.001)
            {
                dynamicCommandList = new CanvasCommandList(sender);
                using var commandSession = dynamicCommandList.CreateDrawingSession();
                commandSession.Clear(Microsoft.UI.Colors.Transparent);
                commandSession.DrawTextLayout(entry.Layout, 0, 0, Color.FromArgb(255, 136, 136, 136));
                effectSource = dynamicCommandList;
            }

            try
            {
                using var blur = new GaussianBlurEffect
                {
                    Source = effectSource,
                    BlurAmount = (float)visual.BlurAmount,
                    BorderMode = EffectBorderMode.Soft,
                    Optimization = EffectOptimization.Balanced
                };
                var layoutBounds = entry.Layout.LayoutBounds;
                var padding = visual.BlurAmount * BlurPaddingMultiplier;
                using var crop = new CropEffect
                {
                    Source = blur,
                    SourceRectangle = new Rect(
                        Math.Max(0, layoutBounds.X - padding),
                        Math.Max(0, layoutBounds.Y - padding),
                        Math.Max(1, layoutBounds.Width + (padding * 2)),
                        Math.Max(1, layoutBounds.Height + (padding * 2))),
                    BorderMode = EffectBorderMode.Soft
                };
                drawingSession.DrawImage(crop, x, y);
                drawingSession.DrawTextLayout(
                    entry.Layout,
                    x,
                    y,
                    ApplyOpacity(Color.FromArgb(255, 136, 136, 136), CrispOverlayOpacity));
                // 副行不进模糊管线，始终清晰直绘，保证逐字点亮动效在小字号下可见。
                DrawCompanionLayouts(drawingSession, entry, x, y);
            }
            finally
            {
                dynamicCommandList?.Dispose();
            }
        }
        finally
        {
            drawingSession.Transform = originalTransform;
        }
    }

    private void ResetAndApplyKaraokeBrushes(
        LyricsLayoutEntry entry,
        LyricsLineVisualState visual)
    {
        if (_baseBrush is null || _highlightBrush is null || _partialBrush is null || entry.Text.Length == 0)
        {
            return;
        }

        entry.Layout.SetBrush(0, entry.Text.Length, _baseBrush);
        if (entry.PaintableIndexes.Length == 0 || visual.KaraokeProgress <= 0 || visual.Activation <= 0)
        {
            return;
        }

        var highlightColor = InterpolateColor(
            Color.FromArgb(255, 136, 136, 136),
            Microsoft.UI.Colors.White,
            visual.Activation);
        _highlightBrush.Color = highlightColor;
        var exactProgress = Math.Clamp(visual.KaraokeProgress, 0, 1) * entry.PaintableIndexes.Length;
        var completed = Math.Min(entry.PaintableIndexes.Length, (int)Math.Floor(exactProgress));
        for (var index = 0; index < completed; index++)
        {
            entry.Layout.SetBrush(entry.PaintableIndexes[index], 1, _highlightBrush);
        }

        if (completed >= entry.PaintableIndexes.Length)
        {
            return;
        }

        var partial = exactProgress - completed;
        if (partial <= 0)
        {
            return;
        }

        _partialBrush.Color = InterpolateColor(
            Color.FromArgb(255, 136, 136, 136),
            highlightColor,
            partial);
        entry.Layout.SetBrush(entry.PaintableIndexes[completed], 1, _partialBrush);
    }

    private void ApplyCompanionKaraokeBrushes(
        LyricsLayoutEntry entry,
        LyricsLineVisualState visual)
    {
        if (_baseBrush is null || _companionHighlightBrush is null)
        {
            return;
        }

        var position = _lastPositionSeconds;
        foreach (var companion in entry.Companions)
        {
            if (companion.CharacterCount == 0)
            {
                continue;
            }

            companion.Layout.SetBrush(0, companion.CharacterCount, _baseBrush);
            if (companion.PaintableIndexes.Length == 0 || visual.Activation <= 0.001)
            {
                continue;
            }

            double progress;
            if (companion.Segments.Count > 0)
            {
                // 和声副行：按自身段起止与真实播放位置点亮，
                // 和声错开主唱时间轴时仍保持正确的点亮节奏。
                var firstStart = companion.Segments[0].StartSeconds;
                var lastEnd = companion.Segments[^1].EndSeconds;
                var span = lastEnd - firstStart;
                progress = span > 0.001
                    ? Math.Clamp((position - firstStart) / span, 0d, 1d)
                    : position >= firstStart ? 1d : 0d;
            }
            else
            {
                // 行级副行（翻译/罗马音）：跟随主行逐字进度同步点亮。
                progress = visual.KaraokeProgress;
            }

            var highlightColor = InterpolateColor(
                Color.FromArgb(255, 136, 136, 136),
                Microsoft.UI.Colors.White,
                visual.Activation);
            _companionHighlightBrush.Color = highlightColor;
            var exactProgress = Math.Clamp(progress, 0, 1) * companion.PaintableIndexes.Length;
            var completed = Math.Min(companion.PaintableIndexes.Length, (int)Math.Floor(exactProgress));
            for (var index = 0; index < completed; index++)
            {
                companion.Layout.SetBrush(companion.PaintableIndexes[index], 1, _companionHighlightBrush);
            }

            if (completed >= companion.PaintableIndexes.Length)
            {
                continue;
            }

            var partial = exactProgress - completed;
            if (partial <= 0)
            {
                continue;
            }

            companion.PartialBrush.Color = InterpolateColor(
                Color.FromArgb(255, 136, 136, 136),
                highlightColor,
                partial);
            companion.Layout.SetBrush(companion.PaintableIndexes[completed], 1, companion.PartialBrush);
        }
    }

    private static void DrawCompanionLayouts(
        CanvasDrawingSession drawingSession,
        LyricsLayoutEntry entry,
        float x,
        float y)
    {
        if (entry.Companions.Length == 0)
        {
            return;
        }

        var companionY = y + (float)entry.PrimaryContentHeight + CompanionLineGap;
        foreach (var companion in entry.Companions)
        {
            drawingSession.DrawTextLayout(companion.Layout, x, companionY, CompanionTextColor);
            companionY += companion.Layout.LineMetrics.Sum(metric => (float)metric.Height);
        }
    }

    private static float ResolvePrimaryFontSize(float stageWidth)
    {
        if (stageWidth <= 1000f)
        {
            return 45f;
        }

        if (stageWidth <= 1400f)
        {
            return 50f;
        }

        if (stageWidth <= 1800f)
        {
            return 56f;
        }

        return 62f;
    }

    private void RebuildLayoutCache()
    {
        if (!_resourcesReady || !IsLoaded || StageCanvas.ActualWidth <= 0)
        {
            return;
        }

        DisposeLayoutCache();
        var layoutWidth = Math.Max(1, (float)StageCanvas.ActualWidth - (HorizontalPadding * 2));
        var primaryFontSize = ResolvePrimaryFontSize((float)StageCanvas.ActualWidth);
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display",
            FontSize = primaryFontSize,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        using var companionFormat = new CanvasTextFormat
        {
            FontFamily = "Segoe UI Variable Display",
            FontSize = MathF.Round(primaryFontSize * 0.42f),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Top,
            WordWrapping = CanvasWordWrapping.Wrap
        };
        var heights = new double[_lyrics.Count];
        for (var index = 0; index < _lyrics.Count; index++)
        {
            var text = _lyrics[index].Text ?? string.Empty;
            var layout = new CanvasTextLayout(StageCanvas, text, format, layoutWidth, 4096);
            var contentHeight = layout.LineMetrics.Sum(metric => (double)metric.Height);

            var companionTexts = _lyrics[index].CompanionLines ?? Array.Empty<LyricCompanionModel>();
            var companions = new CompanionEntry[companionTexts.Count];
            var companionContentHeight = 0d;
            for (var companionIndex = 0; companionIndex < companionTexts.Count; companionIndex++)
            {
                var companionText = companionTexts[companionIndex].Text ?? string.Empty;
                var companionLayout = new CanvasTextLayout(
                    StageCanvas,
                    companionText,
                    companionFormat,
                    layoutWidth,
                    512);
                companions[companionIndex] = new CompanionEntry(
                    companionLayout,
                    companionText.Length,
                    companionText
                        .Select((character, characterIndex) => (character, characterIndex))
                        .Where(item => !char.IsWhiteSpace(item.character))
                        .Select(item => item.characterIndex)
                        .ToArray(),
                    companionTexts[companionIndex].TimedSegments,
                    new CanvasSolidColorBrush(StageCanvas, Microsoft.UI.Colors.White));
                companionContentHeight += companionLayout.LineMetrics.Sum(metric => (double)metric.Height);
            }

            var hasCompanions = companions.Length > 0;
            var height = Math.Max(
                MinimumLineHeight,
                contentHeight + 16 + (hasCompanions ? CompanionLineGap + companionContentHeight + 6 : 0));
            var staticCommandList = new CanvasCommandList(StageCanvas);
            using (var staticSession = staticCommandList.CreateDrawingSession())
            {
                staticSession.Clear(Microsoft.UI.Colors.Transparent);
                staticSession.DrawTextLayout(layout, 0, 0, Color.FromArgb(255, 136, 136, 136));
            }

            heights[index] = height;
            _layoutCache.Add(new LyricsLayoutEntry(
                text,
                layout,
                companions,
                staticCommandList,
                height,
                contentHeight,
                text.Select((character, characterIndex) => (character, characterIndex))
                    .Where(item => !char.IsWhiteSpace(item.character))
                    .Select(item => item.characterIndex)
                    .ToArray()));
        }

        _scene.SetLineMetrics(heights, LineGap);
        StageCanvas.Invalidate();
    }

    private void StageCanvas_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(StageCanvas).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        _scene.ScrollBy(-delta * 0.35, ActualHeight);
        NotifyManualBrowseInteraction();
        StageCanvas.Invalidate();
        e.Handled = true;
    }

    private void StageCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(StageCanvas);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _capturedPointerId = e.Pointer.PointerId;
        _pointerPressedAt = point.Position;
        _lastPointerPosition = point.Position;
        _dragExceededThreshold = false;
        _isDragging = StageCanvas.CapturePointer(e.Pointer);
    }

    private void StageCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || e.Pointer.PointerId != _capturedPointerId)
        {
            return;
        }

        var position = e.GetCurrentPoint(StageCanvas).Position;
        if (!_dragExceededThreshold && Distance(position, _pointerPressedAt) >= DragThreshold)
        {
            _dragExceededThreshold = true;
        }

        if (_dragExceededThreshold)
        {
            _scene.ScrollBy(_lastPointerPosition.Y - position.Y, ActualHeight);
            NotifyManualBrowseInteraction();
            StageCanvas.Invalidate();
        }

        _lastPointerPosition = position;
        e.Handled = _dragExceededThreshold;
    }

    private void StageCanvas_PointerReleased(object sender, PointerRoutedEventArgs e) =>
        ReleasePointer(e);

    private void StageCanvas_PointerCanceled(object sender, PointerRoutedEventArgs e) =>
        ReleasePointer(e);

    private void StageCanvas_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_scene.IsManualBrowsing)
        {
            ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(true));
        }
    }

    private void StageCanvas_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_dragExceededThreshold)
        {
            _dragExceededThreshold = false;
            return;
        }

        var position = e.GetPosition(StageCanvas);
        var lineIndex = _scene.HitTest(position.Y, ActualHeight);
        if (lineIndex >= 0)
        {
            LyricInvoked?.Invoke(this, new LyricsLineInvokedEventArgs(lineIndex));
            e.Handled = true;
        }
    }

    private void ReleasePointer(PointerRoutedEventArgs e)
    {
        if (!_isDragging || e.Pointer.PointerId != _capturedPointerId)
        {
            return;
        }

        StageCanvas.ReleasePointerCapture(e.Pointer);
        _isDragging = false;
        _capturedPointerId = 0;
        if (_scene.IsManualBrowsing)
        {
            NotifyManualBrowseInteraction();
        }
    }

    private void BeginManualBrowse()
    {
        _scene.BeginManualBrowse();
        NotifyManualBrowseInteraction();
        StageCanvas.Invalidate();
    }

    private void NotifyManualBrowseInteraction() =>
        ManualBrowseChanged?.Invoke(this, new LyricsManualBrowseChangedEventArgs(true));

    private void EnsureRenderingSubscription()
    {
        if (_renderingSubscribed || !IsLoaded || !_scene.NeedsFrames)
        {
            return;
        }

        CompositionTarget.Rendering += CompositionTarget_Rendering;
        _renderingSubscribed = true;
    }

    private void StopRenderingSubscription()
    {
        if (!_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _renderingSubscribed = false;
    }

    private void EnsureClockStarted()
    {
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }
    }

    private void UpdateAccessibleLine()
    {
        if (_lastAccessibleLine == _scene.ActiveIndex)
        {
            return;
        }

        _lastAccessibleLine = _scene.ActiveIndex;
        AutomationProperties.SetName(this, AccessibleLyricText);
        FrameworkElementAutomationPeer.FromElement(this)?.RaiseAutomationEvent(
            AutomationEvents.LiveRegionChanged);
    }

    private void DisposeLayoutCache()
    {
        foreach (var entry in _layoutCache)
        {
            entry.Dispose();
        }

        _layoutCache.Clear();
    }

    private void DisposeBrushes()
    {
        _baseBrush?.Dispose();
        _highlightBrush?.Dispose();
        _partialBrush?.Dispose();
        _companionHighlightBrush?.Dispose();
        _baseBrush = null;
        _highlightBrush = null;
        _partialBrush = null;
        _companionHighlightBrush = null;
    }

    private static double Distance(Point first, Point second)
    {
        var x = first.X - second.X;
        var y = first.Y - second.Y;
        return Math.Sqrt((x * x) + (y * y));
    }

    private static Color InterpolateColor(Color from, Color to, double progress)
    {
        var amount = Math.Clamp(progress, 0, 1);
        return Color.FromArgb(
            (byte)(from.A + ((to.A - from.A) * amount)),
            (byte)(from.R + ((to.R - from.R) * amount)),
            (byte)(from.G + ((to.G - from.G) * amount)),
            (byte)(from.B + ((to.B - from.B) * amount)));
    }

    private static Color ApplyOpacity(Color color, float opacity) => Color.FromArgb(
        (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 1)),
        color.R,
        color.G,
        color.B);

    private sealed class CompanionEntry(
        CanvasTextLayout layout,
        int characterCount,
        int[] paintableIndexes,
        IReadOnlyList<LyricSegmentModel> segments,
        CanvasSolidColorBrush partialBrush) : IDisposable
    {
        public CanvasTextLayout Layout { get; } = layout;
        public int CharacterCount { get; } = characterCount;
        public int[] PaintableIndexes { get; } = paintableIndexes;
        public IReadOnlyList<LyricSegmentModel> Segments { get; } = segments;
        public CanvasSolidColorBrush PartialBrush { get; } = partialBrush;

        public void Dispose()
        {
            Layout.Dispose();
            PartialBrush.Dispose();
        }
    }

    private sealed class LyricsLayoutEntry(
        string text,
        CanvasTextLayout layout,
        CompanionEntry[] companions,
        CanvasCommandList staticCommandList,
        double height,
        double primaryContentHeight,
        int[] paintableIndexes) : IDisposable
    {
        public string Text { get; } = text;
        public CanvasTextLayout Layout { get; } = layout;
        public CompanionEntry[] Companions { get; } = companions;
        public CanvasCommandList StaticCommandList { get; } = staticCommandList;
        public double Height { get; } = height;
        public double PrimaryContentHeight { get; } = primaryContentHeight;
        public int[] PaintableIndexes { get; } = paintableIndexes;

        public void Dispose()
        {
            StaticCommandList.Dispose();
            Layout.Dispose();
            foreach (var companion in Companions)
            {
                companion.Dispose();
            }
        }
    }

    private sealed class LyricsStageAutomationPeer(LyricsStageControl owner)
        : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetNameCore() => owner.AccessibleLyricText;

        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Text;

        protected override string GetLocalizedControlTypeCore() => "lyrics";
    }
}
