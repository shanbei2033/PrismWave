using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PrismWave_WinUI.Converters;

public sealed class CoverImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var source = value as string;
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return new BitmapImage(uri);
            }

            if (File.Exists(source))
            {
                return new BitmapImage(new Uri(source));
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
}
