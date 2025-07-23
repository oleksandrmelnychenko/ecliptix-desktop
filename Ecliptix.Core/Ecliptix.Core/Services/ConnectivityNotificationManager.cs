using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Ecliptix.Core.Controls;

namespace Ecliptix.Core.Services;

public class ConnectivityNotificationManager
{
    private NetworkStatusNotification _currentNotification;
    private Panel _parentContainer;
    private readonly IBrush _notificationBackground = new SolidColorBrush(Color.FromRgb(0x5b, 0x5e, 0x63));
    private CancellationTokenSource _autoHideCts;

    public ConnectivityNotificationManager(Panel parentContainer)
    {
        _parentContainer = parentContainer;
    }

    public async Task ShowConnectivityStatus(bool isConnected)
    {
        // Cancel any pending auto-hide
        _autoHideCts?.Cancel();
        _autoHideCts?.Dispose();

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

        // Auto-hide after 2 seconds if connected
        if (isConnected)
        {
            _autoHideCts = new CancellationTokenSource();
            _ = Task.Delay(TimeSpan.FromSeconds(2), _autoHideCts.Token)
                .ContinueWith(async _ =>
                {
                    if (!_autoHideCts.Token.IsCancellationRequested)
                    {
                        await HideCurrentNotification();
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    private void SetNotificationContent(bool isConnected)
    {
        var (statusText, ellipseColor, iconPath) = GetConnectivityConfiguration(isConnected);
        
        _currentNotification.StatusText = statusText;
        _currentNotification.StatusBackground = _notificationBackground;
        _currentNotification.EllipseColor = ellipseColor;
        _currentNotification.IconPath = iconPath;
    }

    private async Task UpdateExistingNotification(bool isConnected)
    {
        var (statusText, ellipseColor, iconPath) = GetConnectivityConfiguration(isConnected);
        await _currentNotification.UpdateStatusWithAnimation(statusText, ellipseColor, iconPath);
    }

    private (string statusText, IBrush ellipseColor, string iconPath) GetConnectivityConfiguration(bool isConnected)
    {
        if (isConnected)
        {
            return (
                "Connection Established",
                new SolidColorBrush(Color.FromRgb(108, 217, 134)), // Light green
                "M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" // Check circle icon
            );
        }
        else
        {
            return (
                "No Internet Connection",
                new SolidColorBrush(Color.FromRgb(255, 107, 107)), // Light red
                "M10 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2m7-2a9 9 0 11-18 0 9 9 0 0118 0z" // X circle icon
            );
        }
    }

    public async Task HideCurrentNotification()
    {
        if (_currentNotification != null)
        {
            await _currentNotification.HideAsync();
            _parentContainer.Children.Remove(_currentNotification);
            _currentNotification = null;
        }
        
        _autoHideCts?.Cancel();
        _autoHideCts?.Dispose();
        _autoHideCts = null;
    }
}