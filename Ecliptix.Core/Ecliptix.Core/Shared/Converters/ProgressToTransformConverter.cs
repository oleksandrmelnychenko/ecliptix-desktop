using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Transformation;

namespace Ecliptix.Core.Shared.Converters;

public class ProgressToTransformConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double progress)
        {
            return TransformOperations.Parse($"scaleX({progress.ToString(CultureInfo.InvariantCulture)})");
        }
        return TransformOperations.Parse("scaleX(1)");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
