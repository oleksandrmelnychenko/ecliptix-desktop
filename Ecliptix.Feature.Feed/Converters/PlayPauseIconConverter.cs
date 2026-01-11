using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Feature.Feed.Converters;

public sealed class PlayPauseIconConverter : IValueConverter
{
    private const string PLAY_ICON = "M8 5v14l11-7z";
    private const string PAUSE_ICON = "M6 4h4v16H6V4zm8 0h4v16h-4V4z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPlaying)
        {
            return isPlaying ? PAUSE_ICON : PLAY_ICON;
        }
        return PLAY_ICON;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
