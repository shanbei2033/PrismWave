using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace PrismWave_WinUI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is bool b && b;
        if (Invert)
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility visibility && visibility == Visibility.Visible;
        return Invert ? !visible : visible;
    }
}
