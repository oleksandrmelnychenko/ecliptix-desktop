using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class NumberAbbreviationConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int number)
        {
            return number switch
            {
                >= 1000000 => $"{number / 1000000.0:0.#}M",
                >= 1000 => $"{number / 1000.0:0.#}K",
                _ => number.ToString()
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
