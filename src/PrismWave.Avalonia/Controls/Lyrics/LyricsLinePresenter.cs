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
/// 非活动行模糊：文本先离屏渲染为位图，再对 premultiplied BGRA 做可分离盒式模糊
/// （3 次迭代近似高斯），得到平滑弥散的模糊位图；之后每帧只是一次 blit，
/// 与清晰文本按 blurMix 互补透明度交叉淡化。每行仅构建一次，内容变化时重建。
/// IsHitTestVisible 恒为 false，指针交互统一由 LyricsStage 处理。
/// </summary>
public sealed class LyricsLinePresenter : Control
{
    /// <summary>场景 BlurAmount 的归一化基准：非活动行场景值为 1.0（相邻）~1.9（远行），
    /// 取 1.0 让所有非活动行稳态 blurMix=1（纯模糊位图、零清晰层）——
    /// 若低于场景最小值，相邻行会残留部分清晰文本叠在雾上，观感即"运动模糊"（锐核+拖影）。</summary>
    private const double MaxBlurAmount = 1.0;

    /// <summary>降采样倍数：先缩小→小图模糊→双线性放大。放大插值天然抹掉盒式核的方形痕迹，
    /// 产生各向同性的均匀晕开（雾状弥散），避免大图盒式模糊的水平拖影感；计算量也降为 1/N²。</summary>
    private const int DownsampleFactor = 4;

    /// <summary>小图上的盒式模糊半径：降采样 4x + 2 次迭代 r=3 ≈ 原尺寸高斯 σ≈11。</summary>
    private const int SmallBlurRadius = 3;

    /// <summary>小图盒式模糊迭代次数。</summary>
    private const int SmallBlurPasses = 2;

    /// <summary>模糊位图外扩边距（≥3σ≈33），避免弥散尾部被位图边缘截断。</summary>
    private const int BlurPadding = 36;

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
                using var opacityPush = context.PushOpacity(_blurMix);
                context.DrawImage(blurred, new Rect(
                    -BlurPadding,
                    -BlurPadding,
                    blurred.PixelSize.Width,
                    blurred.PixelSize.Height));
            }
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
            using var clipPush = context.PushClip(new Rect(0, 0, revealWidth, LineHeight + 2));
            context.DrawText(_highlightText!, new Point(0, 0));
        }
    }

    /// <summary>
    /// 均匀晕开的雾状模糊（降采样管线）：文本离屏渲染 → 拷出 premultiplied BGRA →
    /// box 平均降采样 4x → 小图 2 次可分离盒式模糊 → 双线性放大回原尺寸 → 写回 WriteableBitmap。
    /// 多级核卷积（box 平均 × 盒式 × 双线性）等效高斯且各向同性，无方向性拖影。
    /// </summary>
    private void BuildBlurredBitmap()
    {
        ReleaseBlurredBitmap();
        if (_baseText is null)
        {
            return;
        }

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

        // 1) box 平均降采样到小图（premultiplied 空间的线性运算，各通道独立合法）
        var smallWidth = Math.Max(1, width / DownsampleFactor);
        var smallHeight = Math.Max(1, height / DownsampleFactor);
        var planes = new float[4][];
        var scratch = new float[smallWidth * smallHeight];
        for (var channel = 0; channel < 4; channel++)
        {
            var plane = new float[smallWidth * smallHeight];
            for (var sy = 0; sy < smallHeight; sy++)
            {
                for (var sx = 0; sx < smallWidth; sx++)
                {
                    var x0 = sx * DownsampleFactor;
                    var y0 = sy * DownsampleFactor;
                    var x1 = Math.Min(width, x0 + DownsampleFactor);
                    var y1 = Math.Min(height, y0 + DownsampleFactor);
                    float sum = 0;
                    var count = 0;
                    for (var y = y0; y < y1; y++)
                    {
                        var row = y * width;
                        for (var x = x0; x < x1; x++)
                        {
                            sum += pixels[((row + x) * 4) + channel];
                            count++;
                        }
                    }

                    plane[(sy * smallWidth) + sx] = sum / Math.Max(1, count);
                }
            }

            // 2) 小图盒式模糊
            BoxBlur(plane, scratch, smallWidth, smallHeight, SmallBlurRadius, SmallBlurPasses);
            planes[channel] = plane;
        }

        // 3) 双线性放大写回原尺寸
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
                var fy = ((y + 0.5f) / DownsampleFactor) - 0.5f;
                var sy0 = Math.Clamp((int)Math.Floor(fy), 0, smallHeight - 1);
                var sy1 = Math.Min(sy0 + 1, smallHeight - 1);
                var wy = Math.Clamp(fy - sy0, 0f, 1f);
                for (var x = 0; x < width; x++)
                {
                    var fx = ((x + 0.5f) / DownsampleFactor) - 0.5f;
                    var sx0 = Math.Clamp((int)Math.Floor(fx), 0, smallWidth - 1);
                    var sx1 = Math.Min(sx0 + 1, smallWidth - 1);
                    var wx = Math.Clamp(fx - sx0, 0f, 1f);
                    var offset = x * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var plane = planes[channel];
                        var rowA = sy0 * smallWidth;
                        var rowB = sy1 * smallWidth;
                        var top = (plane[rowA + sx0] * (1 - wx)) + (plane[rowA + sx1] * wx);
                        var bottom = (plane[rowB + sx0] * (1 - wx)) + (plane[rowB + sx1] * wx);
                        rowBuffer[offset + channel] = (byte)Math.Clamp(
                            (top * (1 - wy)) + (bottom * wy) + 0.5f, 0, 255);
                    }
                }

                Marshal.Copy(rowBuffer, 0, frame.Address + (y * rowBytes), Math.Min(rowBytes, width * 4));
            }
        }

        _blurredBitmap = bitmap;
    }

    /// <summary>可分离盒式模糊（水平+垂直为一次迭代），滑动窗口 O(1)/像素，边界 clamp。</summary>
    private static void BoxBlur(float[] plane, float[] scratch, int width, int height, int radius, int passes)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            BlurPass(plane, scratch, width, height, radius, horizontal: true);
            BlurPass(scratch, plane, width, height, radius, horizontal: false);
        }
    }

    private static void BlurPass(float[] source, float[] target, int width, int height, int radius, bool horizontal)
    {
        var main = horizontal ? width : height;
        var cross = horizontal ? height : width;
        var window = (radius * 2) + 1;
        for (var line = 0; line < cross; line++)
        {
            var baseIndex = line * main;
            float sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                sum += source[baseIndex + Math.Clamp(k, 0, main - 1)];
            }

            for (var i = 0; i < main; i++)
            {
                target[baseIndex + i] = sum / window;
                sum += source[baseIndex + Math.Clamp(i + radius + 1, 0, main - 1)]
                     - source[baseIndex + Math.Clamp(i - radius, 0, main - 1)];
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
