using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Shared.Converters;

public static class MathConverters
{
    public static readonly IValueConverter Add = new AddConverter();
    public static readonly IValueConverter Subtract = new SubtractConverter();
    public static readonly IValueConverter Multiply = new MultiplyConverter();
    public static readonly IValueConverter Divide = new DivideConverter();
}

public class AddConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double doubleValue when parameter is string paramStr && double.TryParse(paramStr, out double param) =>
                doubleValue + param,
            int intValue when parameter is string paramStr2 && int.TryParse(paramStr2, out int param2) => intValue +
                param2,
            _ => value
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Reverse conversion is not supported for math operations");
    }
}

public class SubtractConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter is string paramStr && double.TryParse(paramStr, out double param))
        {
            return doubleValue - param;
        }

        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Reverse conversion is not supported for math operations");
    }
}

public class MultiplyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter is string paramStr && double.TryParse(paramStr, out double param))
        {
            return doubleValue * param;
        }

        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Reverse conversion is not supported for math operations");
    }
}

public class DivideConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue && parameter is string paramStr &&
            double.TryParse(paramStr, out double param) && Math.Abs(param) > double.Epsilon)
        {
            return doubleValue / param;
        }

        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Reverse conversion is not supported for math operations");
    }
}
