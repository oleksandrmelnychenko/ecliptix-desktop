using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Feed.Converters;

public sealed class GreaterThanOneConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 1;
        }

        if (value is long longValue)
        {
            return longValue > 1;
        }

        if (value is double doubleValue)
        {
            return doubleValue > 1.0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
