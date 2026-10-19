using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BOH.Models;

namespace BOH.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Log line colour; the colours match the theme brushes in App.xaml.</summary>
public class LogLevelToColorConverter : IValueConverter
{
    private static readonly Brush Success = Frozen(0x00, 0xE6, 0x76);
    private static readonly Brush Warning = Frozen(0xFF, 0xB3, 0x00);
    private static readonly Brush Error = Frozen(0xFF, 0x3D, 0x57);
    private static readonly Brush Info = Frozen(0x8A, 0x98, 0xB6);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        LogLevel.Success => Success,
        LogLevel.Warning => Warning,
        LogLevel.Error => Error,
        _ => Info,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Status dot of the scheduled task: green when it exists.</summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Color.FromRgb(0x00, 0xE6, 0x76) : Color.FromRgb(0x5A, 0x68, 0x85);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class TaskExistsTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Update schedule" : "Create schedule";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ScheduleTypeEqualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ScheduleType st && parameter is string s && Enum.TryParse<ScheduleType>(s, out var target) && st == target;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string s && Enum.TryParse<ScheduleType>(s, out var result)
            ? result
            : Binding.DoNothing;
}
