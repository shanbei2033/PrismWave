using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PrismWave.Avalonia.Controls.Media;

/// <summary>
/// 稳定封面图：加载完成后再切换源并做 180ms 淡入，避免刷新时闪烁（对齐 WinUI StableCoverImage）。
/// 支持可选圆角裁剪（Avalonia Border.CornerRadius 不裁剪子内容，需在控件内自行 Clip）。
/// 位图统一由 CoverBitmapCache 持有（命中免下载/解码/淡入，容器回收与页面重建不再重复加载）；
/// 解码按 DecodeMaxEdge 降采样，控制内存与 GPU 纹理占用。
/// </summary>
public sealed class StableCoverImage : Panel
{
    private static readonly System.Net.Http.HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    public static readonly StyledProperty<string?> SourcePathProperty = AvaloniaProperty.Register<
        StableCoverImage, string?>(nameof(SourcePath));

    public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<
        StableCoverImage, Stretch>(nameof(Stretch), Stretch.UniformToFill);

    public static readonly StyledProperty<double> CornerRadiusProperty = AvaloniaProperty.Register<
        StableCoverImage, double>(nameof(CornerRadius));

    public static readonly StyledProperty<int> DecodeMaxEdgeProperty = AvaloniaProperty.Register<
        StableCoverImage, int>(nameof(DecodeMaxEdge), 1600);

    private readonly Image _currentImage;
    private Image? _pendingImage;
    private string? _requestedSource;
    private int _loadRevision;
    private Size _lastArrangedSize;

    public StableCoverImage()
    {
        _currentImage = CreateImage();
        Children.Add(_currentImage);
    }

    public string? SourcePath
    {
        get => GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>圆角半径（逻辑像素）。设置后封面内容按圆角矩形裁剪。</summary>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>解码时最长边上限，超过则等比降采样。0 表示不限制。</summary>
    public int DecodeMaxEdge
    {
        get => GetValue(DecodeMaxEdgeProperty);
        set => SetValue(DecodeMaxEdgeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourcePathProperty)
        {
            LoadSource(change.GetNewValue<string?>());
        }
        else if (change.Property == StretchProperty)
        {
            var stretch = change.GetNewValue<Stretch>();
            _currentImage.Stretch = stretch;
            if (_pendingImage is not null)
            {
                _pendingImage.Stretch = stretch;
            }
        }
        else if (change.Property == CornerRadiusProperty)
        {
            UpdateClip(_lastArrangedSize);
        }
    }

    private void LoadSource(string? source)
    {
        if (string.Equals(_requestedSource, source, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _requestedSource = source;
        var revision = ++_loadRevision;
        PromotePendingImage();

        if (string.IsNullOrWhiteSpace(source))
        {
            RemoveCurrentImage();
            return;
        }

        // StyledProperty 只能在 UI 线程读取，先捕获解码上限再进后台。
        var maxEdge = DecodeMaxEdge;
        if (CoverBitmapCache.TryGet(source, maxEdge, out var cached) && cached is not null)
        {
            // 缓存命中：免下载/解码/淡入，直接切源。
            RemoveCurrentImage();
            _currentImage.Source = cached;
            return;
        }

        _ = LoadBitmapAsync(source, revision, maxEdge);
    }

    private async Task LoadBitmapAsync(string source, int revision, int maxEdge)
    {
        Bitmap? bitmap;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                using var stream = await SharedHttpClient.GetStreamAsync(uri).ConfigureAwait(false);

                // 网络流不可 seek，Skia 解码需先完整缓冲到内存。
                using var buffered = new MemoryStream();
                await stream.CopyToAsync(buffered).ConfigureAwait(false);
                buffered.Position = 0;
                bitmap = DecodeBitmap(buffered, maxEdge);
            }
            else if (File.Exists(source))
            {
                await using var fileStream = File.OpenRead(source);
                bitmap = DecodeBitmap(fileStream, maxEdge);
            }
            else
            {
                return;
            }
        }
        catch (Exception exception)
        {
            // 封面加载失败不作为播放错误呈现，但记录诊断日志。
            PrismWave_WinUI.Infrastructure.StartupLog.Write(
                $"cover.load.failed: source={source}: {exception.GetType().Name}: {exception.Message}");
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 位图所有权移交缓存（缓存仅 UI 线程访问）；先入库再检查 revision，重复封面仍可供其他控件复用。
            CoverBitmapCache.Put(source, maxEdge, bitmap);
            if (revision != _loadRevision)
            {
                return;
            }

            PromotePendingImage();
            var pending = CreateImage();
            pending.Opacity = 0;
            pending.Source = bitmap;
            _pendingImage = pending;
            Children.Add(pending);

            // 淡入 180ms 后叠在新图上方（下一帧再置 1，保证过渡生效）
            pending.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(180)
                }
            };
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (revision != _loadRevision || !ReferenceEquals(_pendingImage, pending))
                    {
                        return;
                    }

                    pending.Opacity = 1;
                },
                DispatcherPriority.Render);
        });
    }

    private static Bitmap DecodeBitmap(Stream stream, int maxEdge)
    {
        var original = new Bitmap(stream);
        if (maxEdge <= 0)
        {
            return original;
        }

        var width = original.PixelSize.Width;
        var height = original.PixelSize.Height;
        var longestEdge = Math.Max(width, height);
        if (longestEdge <= maxEdge)
        {
            return original;
        }

        var scale = maxEdge / (double)longestEdge;
        var target = new PixelSize(
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
        var scaled = original.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
        original.Dispose();
        return scaled;
    }

    private Image CreateImage()
    {
        return new Image
        {
            Stretch = Stretch,
            IsHitTestVisible = false
        };
    }

    /// <summary>把已淡入完成的 pending 提升为 current 并释放旧位图，保证旧图生命周期正确结束。</summary>
    private void PromotePendingImage()
    {
        if (_pendingImage is null)
        {
            return;
        }

        var promoted = _pendingImage;
        _pendingImage = null;
        promoted.Transitions = null;

        RemoveCurrentImage();
        _currentImage.Source = promoted.Source;
        promoted.Source = null;
        Children.Remove(promoted);
    }

    private void RemoveCurrentImage()
    {
        // 位图所有权归 CoverBitmapCache，不在此 Dispose（可能仍被其他控件引用）。
        _currentImage.Source = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = default(Size);
        foreach (var child in Children)
        {
            child.Measure(availableSize);
            size = new Size(
                Math.Max(size.Width, child.DesiredSize.Width),
                Math.Max(size.Height, child.DesiredSize.Height));
        }

        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(finalSize));
        }

        UpdateClip(finalSize);
        return finalSize;
    }

    private void UpdateClip(Size size)
    {
        _lastArrangedSize = size;
        var radius = CornerRadius;
        if (radius <= 0.5 || size.Width <= 0 || size.Height <= 0)
        {
            Clip = null;
            return;
        }

        Clip = BuildRoundedRectGeometry(size, radius);
    }

    private static Geometry BuildRoundedRectGeometry(Size size, double radius)
    {
        var width = size.Width;
        var height = size.Height;
        radius = Math.Min(radius, Math.Min(width / 2, height / 2));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(radius, 0), true);
            context.LineTo(new Point(width - radius, 0));
            context.ArcTo(new Point(width, radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(width, height - radius));
            context.ArcTo(new Point(width - radius, height), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(radius, height));
            context.ArcTo(new Point(0, height - radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            context.LineTo(new Point(0, radius));
            context.ArcTo(new Point(radius, 0), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        }

        return geometry;
    }
}
