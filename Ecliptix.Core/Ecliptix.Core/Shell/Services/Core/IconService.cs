using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Shell.Services.Core;

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
        foreach (Uri uri in GetPlatformIconUris())
        {
            if (!AssetLoader.Exists(uri))
            {
                continue;
            }

            try
            {
                Bitmap bitmap = new(AssetLoader.Open(uri));
                return Option<WindowIcon>.Some(new WindowIcon(bitmap));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to load application icon from {IconUri}", uri);
            }
        }

        Log.Warning("Failed to load application icon");
        return Option<WindowIcon>.None;
    }

    private static IEnumerable<Uri> GetPlatformIconUris()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Windows/ecliptix.ico");
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return new Uri("avares://Ecliptix.Core/Assets/Branding/Logos/logo_256x256.png");
        }

        if (OperatingSystem.IsLinux())
        {
            yield return new Uri("avares://Ecliptix.Core/Assets/Branding/Platform/Linux/EcliptixLogo.png");
        }

        yield return new Uri("avares://Ecliptix.Core/Assets/Branding/Logos/logo_256x256.png");
    }
}
