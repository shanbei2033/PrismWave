using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PrismWave_WinUI.Converters;

public sealed class CoverImageSourceConverter : IValueConverter
{
    private static readonly Dictionary<string, WeakReference<BitmapImage>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    private const int MaxCacheEntries = 80;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var source = value as string;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        var decodeWidth = parameter is int pixels ? pixels : 256;

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return GetOrCreateBitmap(uri, decodeWidth);
            }

            if (File.Exists(source))
            {
                return GetOrCreateBitmap(new Uri(source), decodeWidth);
            }
        }
        catch
        {
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }

    private static BitmapImage GetOrCreateBitmap(Uri uri, int decodePixelWidth)
    {
        var key = $"{decodePixelWidth}:{uri.AbsoluteUri}";
        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var weak) && weak.TryGetTarget(out var cached))
            {
                return cached;
            }

            var bitmap = new BitmapImage(uri) { DecodePixelWidth = decodePixelWidth };
            Cache[key] = new WeakReference<BitmapImage>(bitmap);

            if (Cache.Count > MaxCacheEntries)
            {
                var deadKeys = new List<string>();
                foreach (var pair in Cache)
                {
                    if (!pair.Value.TryGetTarget(out _))
                    {
                        deadKeys.Add(pair.Key);
                    }
                }

                foreach (var deadKey in deadKeys)
                {
                    Cache.Remove(deadKey);
                }

                if (Cache.Count > MaxCacheEntries)
                {
                    var firstKey = Cache.Keys.First();
                    Cache.Remove(firstKey);
                }
            }

            return bitmap;
        }
    }
}
