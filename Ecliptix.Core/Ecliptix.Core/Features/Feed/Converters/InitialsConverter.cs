using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Features.Feed.Converters;

public sealed class InitialsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            return parts[0].Length > 0 ? parts[0][0].ToString().ToUpper() : "?";
        }

        char firstInitial = parts[0].Length > 0 ? parts[0][0] : '?';
        char lastInitial = parts[^1].Length > 0 ? parts[^1][0] : '?';

        return $"{char.ToUpper(firstInitial)}{char.ToUpper(lastInitial)}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
