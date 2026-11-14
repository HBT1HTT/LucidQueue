using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LucidQueue.Models;

namespace LucidQueue.Common;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class EffectBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var effect = value is TaskEffect e ? e : TaskEffect.None;
        var color = effect switch
        {
            TaskEffect.Reboot => Color.FromRgb(0xF2, 0xC8, 0x79),
            TaskEffect.Shutdown => Color.FromRgb(0xF5, 0x8C, 0x8C),
            TaskEffect.Sleep => Color.FromRgb(0xBF, 0xAE, 0xFF),
            _ => Color.FromRgb(0x8F, 0xB8, 0xFF)
        };

        var soft = string.Equals(parameter as string, "soft", StringComparison.OrdinalIgnoreCase);
        var brush = new SolidColorBrush(Color.FromArgb(soft ? (byte)0x24 : (byte)0xFF, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class NotNullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
