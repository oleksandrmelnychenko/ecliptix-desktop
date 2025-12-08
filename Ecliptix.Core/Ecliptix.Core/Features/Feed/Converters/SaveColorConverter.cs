using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Ecliptix.Core.Features.Feed.Converters;

public sealed class SaveColorConverter : IValueConverter
{
    private static readonly SolidColorBrush SavedColor = new(Color.Parse("#3B82F6"));
    private static readonly SolidColorBrush NotSavedColor = new(Color.Parse("#6B7280"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isSaved)
        {
            return isSaved ? SavedColor : NotSavedColor;
        }

        return NotSavedColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
