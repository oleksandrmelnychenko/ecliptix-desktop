namespace Ecliptix.Core.Services.Core;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
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
        Uri? iconUri = GetPlatformIconUri();
        if (iconUri == null) return null;
        try
        {
            Bitmap bitmap = new(AssetLoader.Open(iconUri));
            return new WindowIcon(bitmap);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load application icon");
        }
        return null;
    }

    private static Uri? GetPlatformIconUri()
    {
        if (OperatingSystem.IsWindows())
            return new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Windows/ecliptix.ico");
        if (OperatingSystem.IsMacOS())
            return new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/macOS/EcliptixLogo.icns");
        return OperatingSystem.IsLinux() ? new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Linux/EcliptixLogo.png") : null;
    }
}
