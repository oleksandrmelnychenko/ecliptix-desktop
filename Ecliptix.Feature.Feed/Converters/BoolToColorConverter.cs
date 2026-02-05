using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class BoolToColorConverter : IValueConverter
{
    public IBrush TrueBrush { get; set; } = Brushes.Red;
    public IBrush FalseBrush { get; set; } = new SolidColorBrush(Color.Parse("#6B7280"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? TrueBrush : FalseBrush;
        }
        return FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("BoolToColorConverter does not support ConvertBack.");
    }
}
