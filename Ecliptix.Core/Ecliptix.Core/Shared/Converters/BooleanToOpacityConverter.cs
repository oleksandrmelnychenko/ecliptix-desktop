using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Shared.Converters;

public class BooleanToOpacityConverter : IValueConverter
{
    public double TrueOpacity { get; set; } = 1.0;
    public double FalseOpacity { get; set; } = 0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueOpacity : FalseOpacity;
        }
        return FalseOpacity;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
