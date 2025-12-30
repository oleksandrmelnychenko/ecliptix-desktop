using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Shared.Converters;

public class ResourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        string? key = value.ToString();

        if (key != null)
        {
            object? resource = Application.Current!.FindResource(key);

            return resource;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
