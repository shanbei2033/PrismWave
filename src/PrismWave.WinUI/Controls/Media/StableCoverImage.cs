using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using PrismWave_WinUI.Infrastructure.Animation;
using Windows.UI.ViewManagement;

namespace PrismWave_WinUI.Controls.Media;

public sealed class StableCoverImage : Grid
{
    private const int MaxCacheEntries = 50;
    private static readonly Dictionary<string, WeakReference<BitmapImage>> SharedCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath),
        typeof(string),
        typeof(StableCoverImage),
        new PropertyMetadata(null, OnSourcePathChanged));

    public static readonly DependencyProperty DecodePixelWidthProperty = DependencyProperty.Register(
        nameof(DecodePixelWidth),
        typeof(int),
        typeof(StableCoverImage),
        new PropertyMetadata(256));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(StableCoverImage),
        new PropertyMetadata(Stretch.UniformToFill, OnStretchChanged));

    public static readonly DependencyProperty ImageVerticalAlignmentProperty = DependencyProperty.Register(
        nameof(ImageVerticalAlignment),
        typeof(VerticalAlignment),
        typeof(StableCoverImage),
        new PropertyMetadata(VerticalAlignment.Center, OnImageVerticalAlignmentChanged));

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
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public int DecodePixelWidth
    {
        get => (int)GetValue(DecodePixelWidthProperty);
        set => SetValue(DecodePixelWidthProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public VerticalAlignment ImageVerticalAlignment
    {
        get => (VerticalAlignment)GetValue(ImageVerticalAlignmentProperty);
        set => SetValue(ImageVerticalAlignmentProperty, value);
    }

    private static void OnSourcePathChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((StableCoverImage)sender).LoadSource(args.NewValue as string);
    }

    private static void OnStretchChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (StableCoverImage)sender;
        var stretch = (Stretch)args.NewValue;
        control._currentImage.Stretch = stretch;
        if (control._pendingImage is not null)
        {
            control._pendingImage.Stretch = stretch;
        }
    }

    private static void OnImageVerticalAlignmentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (StableCoverImage)sender;
        var alignment = (VerticalAlignment)args.NewValue;
        control._currentImage.VerticalAlignment = alignment;
        if (control._pendingImage is not null)
        {
            control._pendingImage.VerticalAlignment = alignment;
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

        var uri = CreateSourceUri(source);
        if (uri is null)
        {
            return;
        }

        var bitmap = GetOrCreateBitmap(uri, DecodePixelWidth);

        if (bitmap.PixelWidth > 0)
        {
            _currentImage.Source = bitmap;
            return;
        }

        var pending = CreateImage();
        pending.Opacity = 0;

        pending.ImageOpened += (_, _) =>
        {
            if (revision != _loadRevision || !ReferenceEquals(_pendingImage, pending))
            {
                return;
            }

            if (!ResolveAnimationsEnabled())
            {
                _currentImage.SetValue(Image.SourceProperty, bitmap);
                RemovePendingImage();
                return;
            }

            var fade = new DoubleAnimation
            {
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(180)),
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(fade, pending);
            Storyboard.SetTargetProperty(fade, "Opacity");
            var storyboard = new Storyboard();
            storyboard.Children.Add(fade);
            storyboard.Completed += (_, _) =>
            {
                if (revision != _loadRevision || !ReferenceEquals(_pendingImage, pending))
                {
                    return;
                }

                _currentImage.Source = bitmap;
                RemovePendingImage();
            };
            storyboard.Begin();
        };
        pending.ImageFailed += (_, _) =>
        {
            if (revision == _loadRevision && ReferenceEquals(_pendingImage, pending))
            {
                RemovePendingImage();
            }
        };
        _pendingImage = pending;
        Children.Add(pending);
        pending.Source = bitmap;
    }

    private Image CreateImage()
    {
        return new Image
        {
            Stretch = Stretch,
            VerticalAlignment = ImageVerticalAlignment,
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

    private static BitmapImage GetOrCreateBitmap(Uri uri, int decodePixelWidth)
    {
        var key = $"{decodePixelWidth}:{uri.AbsoluteUri}";
        lock (CacheLock)
        {
            if (SharedCache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached))
            {
                return cached;
            }

            var bitmap = new BitmapImage(uri) { DecodePixelWidth = decodePixelWidth };
            SharedCache[key] = new WeakReference<BitmapImage>(bitmap);

            if (SharedCache.Count > MaxCacheEntries)
            {
                var deadKeys = new List<string>();
                foreach (var pair in SharedCache)
                {
                    if (!pair.Value.TryGetTarget(out _))
                    {
                        deadKeys.Add(pair.Key);
                    }
                }

                foreach (var deadKey in deadKeys)
                {
                    SharedCache.Remove(deadKey);
                }

                if (SharedCache.Count > MaxCacheEntries)
                {
                    var firstKey = SharedCache.Keys.First();
                    SharedCache.Remove(firstKey);
                }
            }

            return bitmap;
        }
    }

    private static Uri? CreateSourceUri(string source)
    {
        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return uri;
            }

            return File.Exists(source) ? new Uri(Path.GetFullPath(source)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool ResolveAnimationsEnabled()
    {
        var systemAnimationsEnabled = true;
        try
        {
            systemAnimationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch
        {
        }

        return MotionPolicy.ShouldAnimate(systemAnimationsEnabled);
    }
}
