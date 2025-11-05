using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Shared.Converters;

public class EqualityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.Equals(parameter) ?? false;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported for one-way equality binding");
    }
}
