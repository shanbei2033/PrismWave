using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace PrismWave.Avalonia.Controls.Media;

/// <summary>
/// 稳定封面图：加载完成后再切换源并做 180ms 淡入，避免刷新时闪烁（对齐 WinUI StableCoverImage）。
/// </summary>
public sealed class StableCoverImage : Panel
{
    public static readonly StyledProperty<string?> SourcePathProperty = AvaloniaProperty.Register<
        StableCoverImage, string?>(nameof(SourcePath));

    public static readonly StyledProperty<Stretch> StretchProperty = AvaloniaProperty.Register<
        StableCoverImage, Stretch>(nameof(Stretch), Stretch.UniformToFill);

    private readonly Image _currentImage;
    private Image? _pendingImage;
    private string? _requestedSource;
    private int _loadRevision;

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
    }

    private void LoadSource(string? source)
    {
        if (string.Equals(_requestedSource, source, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _requestedSource = source;
        var revision = ++_loadRevision;
        RemovePendingImage();

        if (string.IsNullOrWhiteSpace(source))
        {
            _currentImage.Source = null;
            return;
        }

        _ = LoadBitmapAsync(source, revision);
    }

    private async Task LoadBitmapAsync(string source, int revision)
    {
        Bitmap? bitmap;
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(12) };
                using var stream = await http.GetStreamAsync(uri).ConfigureAwait(false);
                bitmap = new Bitmap(stream);
            }
            else if (File.Exists(source))
            {
                await using var fileStream = File.OpenRead(source);
                bitmap = new Bitmap(fileStream);
            }
            else
            {
                return;
            }
        }
        catch
        {
            // 封面加载失败不作为播放错误呈现。
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (revision != _loadRevision)
            {
                bitmap.Dispose();
                return;
            }

            RemovePendingImage();
            var pending = CreateImage();
            pending.Opacity = 0;
            pending.Source = bitmap;
            _pendingImage = pending;
            Children.Add(pending);

            // 淡入 180ms 后替换当前源（下一帧再置 1，保证过渡生效）
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

    private Image CreateImage()
    {
        return new Image
        {
            Stretch = Stretch,
            IsHitTestVisible = false
        };
    }

    private void RemovePendingImage()
    {
        if (_pendingImage is null)
        {
            return;
        }

        Children.Remove(_pendingImage);
        _pendingImage = null;
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

        return finalSize;
    }
}
