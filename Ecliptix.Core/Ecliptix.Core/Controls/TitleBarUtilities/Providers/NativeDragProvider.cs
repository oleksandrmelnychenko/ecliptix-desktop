using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Serilog;

namespace Ecliptix.Core.Controls.TitleBarUtilities.Providers;

public class NativeDragProvider : IDisposable
{
    private readonly Window _window;
    private readonly Action<bool> _onDraggingChanged;

    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;

    public NativeDragProvider(Window window, Action<bool> onDraggingChanged)
    {
        _window = window;
        _onDraggingChanged = onDraggingChanged;
        _window.Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, System.EventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Properties.AddWndProcHookCallback(_window, OnWin32WndProc);
            Log.Information("[NATIVE-DRAG] Windows WndProc Hook registered.");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            SetupMacOsNativeTracking();
            Log.Information("[NATIVE-DRAG] macOS AppKit tracking initialized.");
        }
    }

    #region Windows Implementation (WndProc)

    private IntPtr OnWin32WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_ENTERSIZEMOVE)
        {
            _onDraggingChanged(true);
        }
        else if (msg == WM_EXITSIZEMOVE)
        {
            _onDraggingChanged(false);
        }

        return IntPtr.Zero;
    }

    #endregion

    #region macOS Implementation (AppKit Notifications)

    private void SetupMacOsNativeTracking()
    {
        IMacOSTopLevelPlatformHandle? handle = _window.TryGetPlatformHandle() as IMacOSTopLevelPlatformHandle;
        if (handle == null)
        {
            return;
        }

        _window.PositionChanged += (s, e) => _onDraggingChanged(true);

        _window.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
        {
            _onDraggingChanged(false);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
    }

    #endregion

    public void Dispose()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Properties.RemoveWndProcHookCallback(_window, OnWin32WndProc);
        }
        _window.Opened -= OnWindowOpened;
    }
}
