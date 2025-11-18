using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.Features.Feed.Converters;

public sealed class LikeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLiked)
        {
            string resourceKey = isLiked ? "LikeIconFilledGeometry" : "LikeIconGeometry";

            if (Application.Current?.Resources.TryGetResource(resourceKey, null, out object? resource) == true)
            {
                return resource as StreamGeometry;
            }
        }

        return Application.Current?.Resources["LikeIconGeometry"] as StreamGeometry;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
