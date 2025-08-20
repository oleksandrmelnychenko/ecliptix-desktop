using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.Shared.Converters;

public class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        
        if (value is Color color)
        {
            
            return new SolidColorBrush(color);
        }

        
        return Avalonia.Data.BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        
        throw new NotImplementedException();
    }
}