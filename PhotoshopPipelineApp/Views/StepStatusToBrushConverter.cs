using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Views;

public class StepStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush BlueBrush = new(System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush PurpleBrush = new(System.Windows.Media.Color.FromRgb(0x88, 0x78, 0xB0));
    private static readonly SolidColorBrush GrayBrush = new(System.Windows.Media.Color.FromRgb(0x80, 0x80, 0x80));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Completed => BlueBrush,
                StepStatus.Failed => PurpleBrush,
                StepStatus.Pending => PurpleBrush,
                StepStatus.NotApplicable => GrayBrush,
                _ => GrayBrush
            };
        }
        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
