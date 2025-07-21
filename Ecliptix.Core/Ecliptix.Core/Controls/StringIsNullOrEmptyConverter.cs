using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Controls; // Adjust namespace

public class StringIsNullOrEmptyConverter : IValueConverter
{
    public static readonly StringIsNullOrEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
