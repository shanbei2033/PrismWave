using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace PrismWave.Avalonia.Controls.Lyrics;

/// <summary>
/// 单行歌词渲染器：持有该行的 base/highlight FormattedText，绘制当前行内容与卡拉OK揭示。
/// 由 LyricsStage 持有为子视觉并逐帧定位。
/// 非活动行模糊对齐 WinUI 版结构：文本离屏渲染为位图后做真高斯核分离卷积
/// （σ=1.9 DIP 对齐 WinUI BlurAmount 最大档），渲染时模糊底上叠 18% 清晰文本；
/// 与清晰层按 blurMix 交叉淡化。每行仅构建一次，内容变化时重建。
/// IsHitTestVisible 恒为 false，指针交互统一由 LyricsStage 处理。
/// </summary>
public sealed class LyricsLinePresenter : Control
{
    /// <summary>场景 BlurAmount 的归一化基准：非活动行场景值为 1.0（相邻）~1.9（远行），
    /// 取 1.0 让所有非活动行稳态 blurMix=1（纯模糊位图、零清晰层）——
    /// 若低于场景最小值，相邻行会残留部分清晰文本叠在雾上，观感即"运动模糊"（锐核+拖影）。</summary>
    private const double MaxBlurAmount = 1.0;

    /// <summary>模糊高斯 σ（DIP，1px=1DIP 位图内即物理像素）：视觉模糊强度直接对应此值。
    /// 对齐 WinUI 版 GaussianBlurEffect 的 BlurAmount 最大档 1.9（用户验证后的最终选择）。</summary>
    private const double BlurSigma = 1.9;

    /// <summary>模糊位图外扩边距（≥3σ≈6），避免高斯尾部被位图边缘截断发暗。</summary>
    private const int BlurPadding = 8;

    /// <summary>模糊底图上叠加的清晰文本不透明度，对齐 WinUI CrispOverlayOpacity：
    /// 稳态非活动行 = 模糊底 + 微清晰顶，保留字形轮廓感。</summary>
    private const double CrispOverlayOpacity = 0.18;

    private FormattedText? _baseText;
    private FormattedText? _highlightText;
    private readonly MatrixTransform _transform = new();
    private WriteableBitmap? _blurredBitmap;
    private double _blurMix;
    private double _karaokeProgress;
    private double _activation;

    /// <summary>行盒高度（含行距余量），由布局缓存提供。</summary>
    public double LineHeight { get; private set; }

    public LyricsLinePresenter()
    {
        IsHitTestVisible = false;
        // Avalonia 的 RenderTransformOrigin 默认是元素中心（与 WPF 的 (0,0) 不同）；
        // 不显式归零时，矩阵会在缩放外叠加 (中心 × (1-scale)) 的平移，非活动行整体右下漂移、与活动行重叠。
        RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
        RenderTransform = _transform;
    }

    public void SetContent(FormattedText baseText, FormattedText highlightText, double lineHeight)
    {
        _baseText = baseText;
        _highlightText = highlightText;
        LineHeight = lineHeight;
        ReleaseBlurredBitmap();
        InvalidateVisual();
    }

    /// <summary>逐帧应用视觉状态。blurAmount>0.02 时启用模糊位图混合（当前行 blurAmount=0 保持锐利）。</summary>
    public void ApplyState(
        double top,
        double scale,
        double opacity,
        double blurAmount,
        double karaokeProgress,
        double activation)
    {
        // Avalonia Matrix 为行向量约定（p' = p*M1*M2，M1 先作用），关于锚点 (0, LineHeight/2) 缩放：
        // T(0,-a) × S × T(0,+a)，再并上位移。顺序写反会把内容拉向原点。
        var anchorY = LineHeight / 2;
        _transform.Matrix = Matrix.CreateTranslation(0, -anchorY)
            * Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation(0, anchorY + top);
        Opacity = opacity;
        _blurMix = Math.Clamp(blurAmount / MaxBlurAmount, 0, 1);
        if (_blurMix > 0.02 && _blurredBitmap is null && _baseText is not null)
        {
            BuildBlurredBitmap();
        }

        _karaokeProgress = karaokeProgress;
        _activation = activation;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = _baseText is null ? 0 : Math.Max(_baseText.Width, _highlightText?.Width ?? 0);
        return new Size(width, LineHeight);
    }

    public override void Render(DrawingContext context)
    {
        if (_baseText is null)
        {
            return;
        }

        if (_blurMix <= 0.001)
        {
            DrawSharp(context, 1);
        }
        else
        {
            // 过渡期交叉淡化：清晰层透明度按平方衰减，让锐利残影快速消隐，
            // 避免"清晰字核+雾晕"叠加产生运动模糊观感；稳态非活动行 mix=1 时无清晰层。
            if (_blurMix < 0.999)
            {
                var sharpResidue = 1 - _blurMix;
                DrawSharp(context, sharpResidue * sharpResidue);
            }

            if (_blurredBitmap is { } blurred)
            {
                // 位图为 96 DPI、1px=1DIP，按像素尺寸 1:1 blit，与清晰文本精确对位
                using var opacityPush = context.PushOpacity(_blurMix);
                context.DrawImage(blurred, new Rect(
                    -BlurPadding,
                    -BlurPadding,
                    blurred.PixelSize.Width,
                    blurred.PixelSize.Height));
            }

            // 对齐 WinUI CrispOverlayOpacity：模糊底上叠微清晰顶，保留字形轮廓感。
            DrawSharp(context, CrispOverlayOpacity);
        }
    }

    private void DrawSharp(DrawingContext context, double opacity)
    {
        using var opacityPush = context.PushOpacity(opacity);
        context.DrawText(_baseText, new Point(0, 0));
        if (_karaokeProgress > 0 && _activation > 0.001)
        {
            var progress = Math.Clamp(_karaokeProgress, 0, 1);
            var revealWidth = Math.Max(0, _baseText.Width * progress);
            // 高亮白字随 activation 单独衰减：转场期 outgoing 行 progress 定格时
            // 白字快速淡出（灰底保留），避免与新活动行同时全白（两行同亮）。
            using var highlightPush = context.PushOpacity(opacity * Math.Clamp(_activation, 0, 1));
            using var clipPush = context.PushClip(new Rect(0, 0, revealWidth, LineHeight + 2));
            context.DrawText(_highlightText!, new Point(0, 0));
        }
    }

    /// <summary>
    /// 对齐 WinUI 版的模糊结构：文本离屏渲染 → 拷出 premultiplied BGRA →
    /// 真高斯核分离卷积（水平+垂直，σ=1.9 DIP）→ 写回 WriteableBitmap。
    /// 稳态渲染时模糊底图上再叠 18% 清晰文本（CrispOverlayOpacity），保留字形轮廓感。
    /// </summary>
    private void BuildBlurredBitmap()
    {
        ReleaseBlurredBitmap();
        if (_baseText is null)
        {
            return;
        }

        // 位图 96 DPI、1px=1DIP：与 FormattedText 的 DIP 布局同空间，blit 1:1 对位
        var width = Math.Max(1, (int)Math.Ceiling(_baseText.Width)) + BlurPadding * 2;
        var height = Math.Max(1, (int)Math.Ceiling(LineHeight)) + BlurPadding * 2;
        using var source = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = source.CreateDrawingContext())
        {
            context.DrawText(_baseText, new Point(BlurPadding, BlurPadding));
        }

        var pixels = new byte[width * height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            source.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        // 1) premultiplied 空间各通道展开为 float 平面（线性运算，各通道独立合法）
        var pixelCount = width * height;
        var planes = new float[4][];
        var scratch = new float[pixelCount];
        for (var channel = 0; channel < 4; channel++)
        {
            var plane = new float[pixelCount];
            for (var i = 0; i < pixelCount; i++)
            {
                plane[i] = pixels[(i * 4) + channel];
            }

            // 2) 真高斯模糊（分离两遍，σ 单位与位图像素 1:1）
            GaussianBlur(plane, scratch, width, height, BlurSigma);
            planes[channel] = plane;
        }

        // 3) 写回原尺寸 WriteableBitmap
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using (var frame = bitmap.Lock())
        {
            var rowBytes = frame.RowBytes;
            var rowBuffer = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        rowBuffer[offset + channel] = (byte)Math.Clamp(planes[channel][row + x] + 0.5f, 0, 255);
                    }
                }

                Marshal.Copy(rowBuffer, 0, frame.Address + (y * rowBytes), Math.Min(rowBytes, width * 4));
            }
        }

        _blurredBitmap = bitmap;
    }

    /// <summary>真高斯模糊（可分离）：预计算一维高斯权重核（半径 3σ 截断），
    /// 水平、垂直各卷积一遍，O(r)/像素；边界 clamp。</summary>
    private static void GaussianBlur(float[] plane, float[] scratch, int width, int height, double sigma)
    {
        var radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var window = (radius * 2) + 1;
        var kernel = new float[window];
        var sum = 0.0;
        for (var k = -radius; k <= radius; k++)
        {
            var w = Math.Exp(-(k * k) / (2 * sigma * sigma));
            kernel[k + radius] = (float)w;
            sum += w;
        }

        for (var k = 0; k < window; k++)
        {
            kernel[k] /= (float)sum;
        }

        ConvolvePass(plane, scratch, width, height, kernel, radius, horizontal: true);
        ConvolvePass(scratch, plane, width, height, kernel, radius, horizontal: false);
    }

    private static void ConvolvePass(float[] source, float[] target, int width, int height, float[] kernel, int radius, bool horizontal)
    {
        var main = horizontal ? width : height;
        var cross = horizontal ? height : width;
        var window = (radius * 2) + 1;
        for (var line = 0; line < cross; line++)
        {
            var baseIndex = line * main;
            for (var i = 0; i < main; i++)
            {
                float acc = 0;
                for (var k = 0; k < window; k++)
                {
                    var sample = Math.Clamp(i + k - radius, 0, main - 1);
                    acc += kernel[k] * source[baseIndex + sample];
                }

                target[baseIndex + i] = acc;
            }
        }
    }

    private void ReleaseBlurredBitmap()
    {
        _blurredBitmap?.Dispose();
        _blurredBitmap = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ReleaseBlurredBitmap();
    }
}
