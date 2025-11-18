using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Ecliptix.Core.Features.Feed.Converters;

public sealed class PlayPauseIconConverter : IValueConverter
{
    private const string PlayIcon = "M8 5v14l11-7z";
    private const string PauseIcon = "M6 4h4v16H6V4zm8 0h4v16h-4V4z";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPlaying)
        {
            return isPlaying ? PauseIcon : PlayIcon;
        }
        return PlayIcon;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
