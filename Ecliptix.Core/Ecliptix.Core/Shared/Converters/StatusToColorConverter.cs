using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Ecliptix.Core.AppEvents.Network;

namespace Ecliptix.Core.Shared.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is NetworkStatus status)
        {
            return status switch
            {
                NetworkStatus.DataCenterConnected => SolidColorBrush.Parse("#28C940"),
                NetworkStatus.DataCenterConnecting => SolidColorBrush.Parse("#FFBD2E"),
                NetworkStatus.RestoreSecrecyChannel => SolidColorBrush.Parse("#FFBD2E"),
                NetworkStatus.DataCenterDisconnected => SolidColorBrush.Parse("#FF5F57"),
                _ => SolidColorBrush.Parse("#9966CC")
            };
        }
        return SolidColorBrush.Parse("#9966CC");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}