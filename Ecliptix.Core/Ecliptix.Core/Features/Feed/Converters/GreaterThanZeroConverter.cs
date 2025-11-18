using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Features.Feed.Converters;

public sealed class GreaterThanZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }

        if (value is long longValue)
        {
            return longValue > 0;
        }

        if (value is double doubleValue)
        {
            return doubleValue > 0.0;
        }

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
