using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.ViewModels;

public class ColorToBrushConverter : IValueConverter
{
    public static readonly ColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // If the input value is a Color...
        if (value is Color color)
        {
            // ...return a new SolidColorBrush with that color.
            return new SolidColorBrush(color);
        }

        // If the input is not a Color, return a default or do nothing.
        return Avalonia.Data.BindingOperations.DoNothing;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // We don't need to convert from a Brush back to a Color, so this is not implemented.
        throw new NotImplementedException();
    }
}