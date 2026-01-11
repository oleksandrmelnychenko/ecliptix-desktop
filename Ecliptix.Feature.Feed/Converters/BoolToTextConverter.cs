using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class BoolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not bool boolValue || parameter is not string paramString)
        {
            return string.Empty;
        }

        string[] parts = paramString.Split('|');
        if (parts.Length != 2)
        {
            return string.Empty;
        }

        return boolValue ? parts[0] : parts[1];
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
