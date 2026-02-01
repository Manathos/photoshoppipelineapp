using System;
using System.Globalization;
using System.Windows.Data;
using PhotoshopPipelineApp.Models;

namespace PhotoshopPipelineApp.Views;

public class StepStatusToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StepStatus status)
        {
            return status switch
            {
                StepStatus.Completed => "✓",
                StepStatus.Failed => "✗",
                StepStatus.Pending => "✗",
                StepStatus.NotApplicable => "—",
                _ => "·"
            };
        }
        return "·";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
