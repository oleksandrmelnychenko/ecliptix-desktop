using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;


namespace Ecliptix.Core.Features.Feed.Converters;


//TODO, we can move icons to a global context and then this logic gonna work, because it seraches icon in app.axaml
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
        throw new NotImplementedException();
    }
}
