using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Ecliptix.Core.Controls;

namespace Ecliptix.Core.Services;

public class ConnectivityNotificationManager(Panel parentContainer)
{
    private NetworkStatusNotification? _currentNotification;
    private CancellationTokenSource? _autoHideCts;
    private readonly Panel _parentContainer = parentContainer;
    private readonly IBrush _notificationBackground = new SolidColorBrush(Color.FromRgb(43, 48, 51)); // #2b3033

    private const string DisconnectedIconPath =
        "M10.8809 16.15C10.8809 16.0021 10.9101 15.8556 10.967 15.7191C11.024 15.5825 11.1073 15.4586 11.2124 15.3545C11.3175 15.2504 11.4422 15.1681 11.5792 15.1124C11.7163 15.0567 11.8629 15.0287 12.0109 15.03C12.2291 15.034 12.4413 15.1021 12.621 15.226C12.8006 15.3499 12.9399 15.5241 13.0211 15.7266C13.1024 15.9292 13.122 16.1512 13.0778 16.3649C13.0335 16.5786 12.9272 16.7745 12.7722 16.9282C12.6172 17.0818 12.4204 17.1863 12.2063 17.2287C11.9922 17.2711 11.7703 17.2494 11.5685 17.1663C11.3666 17.0833 11.1938 16.9426 11.0715 16.7618C10.9492 16.5811 10.8829 16.3683 10.8809 16.15ZM11.2408 13.42L11.1008 8.20001C11.0875 8.07453 11.1008 7.94766 11.1398 7.82764C11.1787 7.70761 11.2424 7.5971 11.3268 7.5033C11.4112 7.40949 11.5144 7.33449 11.6296 7.28314C11.7449 7.2318 11.8697 7.20526 11.9958 7.20526C12.122 7.20526 12.2468 7.2318 12.3621 7.28314C12.4773 7.33449 12.5805 7.40949 12.6649 7.5033C12.7493 7.5971 12.813 7.70761 12.8519 7.82764C12.8909 7.94766 12.9042 8.07453 12.8909 8.20001L12.7609 13.42C12.7609 13.6215 12.6809 13.8149 12.5383 13.9574C12.3958 14.0999 12.2024 14.18 12.0009 14.18C11.7993 14.18 11.606 14.0999 11.4635 13.9574C11.321 13.8149 11.2408 13.6215 11.2408 13.42Z M12 21.5C17.1086 21.5 21.25 17.3586 21.25 12.25C21.25 7.14137 17.1086 3 12 3C6.89137 3 2.75 7.14137 2.75 12.25C2.75 17.3586 6.89137 21.5 12 21.5Z";

    private const string ConnectedIconPath =
        "M12,2 C17.5228475,2 22,6.4771525 22,12 C22,17.5228475 17.5228475,22 12,22 C6.4771525,22 2,17.5228475 2,12 C2,6.4771525 6.4771525,2 12,2 Z M12,4 C7.581722,4 4,7.581722 4,12 C4,16.418278 7.581722,20 12,20 C16.418278,20 20,16.418278 20,12 C20,7.581722 16.418278,4 12,4 Z M15.2928932,8.29289322 L10,13.5857864 L8.70710678,12.2928932 C8.31658249,11.9023689 7.68341751,11.9023689 7.29289322,12.2928932 C6.90236893,12.6834175 6.90236893,13.3165825 7.29289322,13.7071068 L9.29289322,15.7071068 C9.68341751,16.0976311 10.3165825,16.0976311 10.7071068,15.7071068 L16.7071068,9.70710678 C17.0976311,9.31658249 17.0976311,8.68341751 16.7071068,8.29289322 C16.3165825,7.90236893 15.6834175,7.90236893 15.2928932,8.29289322 Z";

    public async Task ShowConnectivityStatus(bool isConnected)
    {
        if (_autoHideCts is not null)
        {
            await _autoHideCts.CancelAsync();
            _autoHideCts.Dispose();
            _autoHideCts = null;
        }

        if (_currentNotification == null)
        {
            _currentNotification = new NetworkStatusNotification();
            _parentContainer.Children.Add(_currentNotification);

            SetNotificationContent(isConnected);
            await _currentNotification.ShowAsync();
        }
        else
        {
            await UpdateExistingNotification(isConnected);
        }

        if (isConnected)
        {
            _autoHideCts = new CancellationTokenSource();
            CancellationToken token = _autoHideCts.Token;

            _ = Task.Delay(TimeSpan.FromSeconds(2), token)
                .ContinueWith(async task =>
                {
                    if (!token.IsCancellationRequested && task.IsCompletedSuccessfully)
                    {
                        await HideCurrentNotification();
                    }
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void SetNotificationContent(bool isConnected)
    {
        (string statusText, IBrush ellipseColor, string iconPath) = GetConnectivityConfiguration(isConnected);

        _currentNotification!.StatusText = statusText;
        _currentNotification.StatusBackground = _notificationBackground;
        _currentNotification.EllipseColor = ellipseColor;

        try
        {
            _currentNotification.IconPath = Geometry.Parse(iconPath);
        }
        catch
        {
            _currentNotification.IconPath = Geometry.Parse("M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2z");
        }
    }

    private async Task UpdateExistingNotification(bool isConnected)
    {
        (string statusText, IBrush ellipseColor, string iconPath) = GetConnectivityConfiguration(isConnected);
        await _currentNotification!.UpdateStatusWithAnimation(statusText, ellipseColor, iconPath);
    }

    private (string statusText, IBrush ellipseColor, string iconPath) GetConnectivityConfiguration(bool isConnected)
    {
        if (!isConnected)
        {
            return (
                "No Internet Connection",
                new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                DisconnectedIconPath
            );
        }

        return (
            "Connection Established",
            new SolidColorBrush(Color.FromRgb(108, 217, 134)),
            ConnectedIconPath
        );
    }

    public async Task HideCurrentNotification()
    {
        if (_currentNotification != null)
        {
            await _currentNotification.HideAsync();
            _parentContainer.Children.Remove(_currentNotification);
            _currentNotification = null;
        }

        if (_autoHideCts != null)
        {
            await _autoHideCts.CancelAsync();
            _autoHideCts.Dispose();
            _autoHideCts = null;
        }
    }
}
