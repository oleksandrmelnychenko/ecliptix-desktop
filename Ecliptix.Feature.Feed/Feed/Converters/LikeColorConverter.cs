using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Feature.Feed.Feed.Converters;

public sealed class LikeColorConverter : IValueConverter
{
    private static readonly SolidColorBrush LikedColor = new(Color.Parse("#FF6D00"));
    private static readonly SolidColorBrush NotLikedColor = new(Color.Parse("#6B7280"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isLiked)
        {
            return isLiked ? LikedColor : NotLikedColor;
        }

        return NotLikedColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
