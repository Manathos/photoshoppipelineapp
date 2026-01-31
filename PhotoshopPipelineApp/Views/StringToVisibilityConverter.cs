using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PhotoshopPipelineApp.Views;

public class StringToVisibilityConverter : IValueConverter
{
    /// <summary>Parameter "ShowWhenEmpty" = Visible when string is empty; "HideWhenEmpty" = Visible when string is not empty.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (value as string) ?? string.Empty;
        var empty = string.IsNullOrWhiteSpace(s);
        var param = (parameter as string) ?? "";
        if (param.Equals("ShowWhenEmpty", StringComparison.OrdinalIgnoreCase))
            return empty ? Visibility.Visible : Visibility.Collapsed;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
