using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using LimitLens.Core.Settings;

namespace LimitLens.App.Converters;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolean && !boolean;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool boolean && !boolean;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StringEqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ThemeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        DashboardTheme.DarkGlass => "Dark",
        DashboardTheme.System => "Follow Windows",
        DashboardTheme.Light => "Light",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class UsageRemainingBrushConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush NormalLight = FrozenBrush(63, 71, 77);
    private static readonly SolidColorBrush NormalDark = FrozenBrush(193, 196, 203);
    private static readonly SolidColorBrush Warning = FrozenBrush(255, 157, 61);
    private static readonly SolidColorBrush Critical = FrozenBrush(255, 99, 112);

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var remaining = values.Length > 0 && values[0] is double number ? number : 100d;
        var theme = values.Length > 1 && values[1] is DashboardTheme selected
            ? selected
            : DashboardTheme.Light;
        return Select(remaining, theme);
    }

    public static SolidColorBrush Select(double remaining, DashboardTheme theme)
    {
        var normal = theme == DashboardTheme.Light ? NormalLight : NormalDark;
        return remaining <= 10 ? Critical : remaining <= 25 ? Warning : normal;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
