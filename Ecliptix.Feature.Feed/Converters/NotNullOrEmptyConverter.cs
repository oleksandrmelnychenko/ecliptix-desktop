using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class NotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string str && !string.IsNullOrWhiteSpace(str);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
