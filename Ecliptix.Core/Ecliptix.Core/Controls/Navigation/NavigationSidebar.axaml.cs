using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Navigation;

public partial class NavigationSidebar : UserControl
{
    public NavigationSidebar()
    {
        AvaloniaXamlLoader.Load(this);
        Resources.Add("IconPathConverter", new IconPathToGeometryConverter());
    }
}

public sealed class IconPathToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string iconKey)
        {
            return null;
        }

        try
        {
            if (Application.Current?.TryGetResource(iconKey, out object? resource) == true && resource is StreamGeometry geometry)
            {
                return geometry;
            }
        }
        catch
        {
            // Resource not found
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
