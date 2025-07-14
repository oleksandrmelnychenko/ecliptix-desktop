namespace Ecliptix.Core.Services;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;

public static class IconService
{
    private static WindowIcon? _cachedIcon;
    
    public static void SetIconForWindow(Window window)
    {
        if (_cachedIcon == null)
        {
            _cachedIcon = LoadPlatformIcon();
        }
        
        if (_cachedIcon != null)
        {
            window.Icon = _cachedIcon;
        }
    }

    private static WindowIcon? LoadPlatformIcon()
    {
        var iconUri = GetPlatformIconUri();
        if (iconUri != null)
        {
            try
            {
                var bitmap = new Bitmap(AssetLoader.Open(iconUri));
                return new WindowIcon(bitmap);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load icon: {ex.Message}");
            }
        }
        return null;
    }

    private static Uri GetPlatformIconUri()
    {
        if (OperatingSystem.IsWindows())
            return new Uri("avares://Ecliptix.Core/Assets/logo.ico");
        else if (OperatingSystem.IsMacOS())
            return new Uri("avares://Ecliptix.Core/Assets/logo.icns");
        else if (OperatingSystem.IsLinux())
            return new Uri("avares://Ecliptix.Core/Assets/logo.png");
        
        return null;
    }
}
