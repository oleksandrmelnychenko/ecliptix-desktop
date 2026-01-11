using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class RelativeTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dateTime)
        {
            TimeSpan difference = DateTime.UtcNow - dateTime;

            return difference switch
            {
                { TotalSeconds: < 60 } => "Just now",
                { TotalMinutes: < 60 } => $"{(int)difference.TotalMinutes}m ago",
                { TotalHours: < 24 } => $"{(int)difference.TotalHours}h ago",
                { TotalDays: < 7 } => $"{(int)difference.TotalDays}d ago",
                { TotalDays: < 30 } => $"{(int)(difference.TotalDays / 7)}w ago",
                _ => dateTime.ToString("MMM dd, yyyy")
            };
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
