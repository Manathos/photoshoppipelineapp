using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PhotoshopPipelineApp.Views;

public class PathToImageSourceConverter : IValueConverter
{
    private const int DecodePixelWidth = 240;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var path = value as string;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var uri = new Uri(path, UriKind.Absolute);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = uri;
            bi.DecodePixelWidth = DecodePixelWidth;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
