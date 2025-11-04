namespace Ecliptix.Core.Services.Core;

using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;
using Utilities;



internal static class IconService
{
    private static Option<WindowIcon> _cachedIcon = Option<WindowIcon>.None;

    public static void SetIconForWindow(Window window)
    {
        _cachedIcon.Or(LoadPlatformIcon)
            .Do(icon =>
            {
                _cachedIcon = Option<WindowIcon>.Some(icon);
                window.Icon = icon;
            });
    }

    private static Option<WindowIcon> LoadPlatformIcon()
    {
        return GetPlatformIconUri()
            .Bind(uri =>
            {
                try
                {
                    Bitmap bitmap = new(AssetLoader.Open(uri));
                    return Option<WindowIcon>.Some(new WindowIcon(bitmap));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load application icon");
                    return Option<WindowIcon>.None;
                }
            });
    }

    private static Option<Uri> GetPlatformIconUri()
    {
        if (OperatingSystem.IsWindows())
        {
            return Option<Uri>.Some(new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Windows/ecliptix.ico"));
        }

        if (OperatingSystem.IsMacOS())
        {
            return Option<Uri>.Some(new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/macOS/EcliptixLogo.icns"));
        }

        if (OperatingSystem.IsLinux())
        {
            return Option<Uri>.Some(new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Linux/EcliptixLogo.png"));
        }

        return Option<Uri>.None;
    }
}
