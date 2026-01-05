using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Ecliptix.Feature.Chats.Chats.Domain.Models;

namespace Ecliptix.Feature.Chats.Chats.Converters;

public class ChatTypeToCornerRadiusConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChatType type)
        {
            return type == ChatType.Personal ? new CornerRadius(24) : new CornerRadius(12);
        }
        return new CornerRadius(24);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
