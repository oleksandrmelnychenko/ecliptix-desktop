using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Feature.Feed.Converters;

public class DynamicIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string resourceKey && !string.IsNullOrEmpty(resourceKey))
        {
            if (Application.Current!.TryGetResource(resourceKey, null, out object? resource))
            {
                return resource as StreamGeometry;
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("DynamicIconConverter does not support ConvertBack.");
    }
}
