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
        if (value is double doubleValue && parameter is string paramStr && double.TryParse(paramStr, out double param))
            return doubleValue + param;

        if (value is int intValue && parameter is string paramStr2 && int.TryParse(paramStr2, out int param2))
            return intValue + param2;

        return value;
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
            return doubleValue - param;

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
            return doubleValue * param;

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
        if (value is double doubleValue && parameter is string paramStr && double.TryParse(paramStr, out double param) && param != 0)
            return doubleValue / param;

        return value;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Reverse conversion is not supported for math operations");
    }
}
