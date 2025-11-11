using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Views.Memberships.Components;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Serilog;

namespace Ecliptix.Core.ViewModels.Core;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
    private readonly IBottomSheetService _bottomSheetService;
    private readonly IApplicationSecureStorageProvider _storageProvider;

    private bool _isDisposed;

    private bool _isMainContentActive = false;

    [Reactive] public WindowState WindowState { get; set; } = WindowState.Normal;
    [Reactive] public object? CurrentContent { get; private set; }

    [Reactive] public double WindowWidth { get; set; }

    [Reactive] public double WindowHeight { get; set; }

    [Reactive] public PixelPoint CurrentPosition { get; set; } = new(0, 0);

    [Reactive] public bool CanResize { get; private set; }

    [Reactive] public string WindowTitle { get; set; }

    public LanguageSelectorViewModel LanguageSelector { get; }

    public TitleBarViewModel TitleBarViewModel { get; }
    public ConnectivityNotificationViewModel ConnectivityNotification { get; }

    public Func<Rect>? GetPrimaryScreenWorkingArea { get; set; }
    public Action? SyncViewModelWithActualWindowSize { get; set; }


    public event Action<PixelPoint>? OnWindowRepositionRequested;


    public MainWindowViewModel(
        IBottomSheetService bottomSheetService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider storageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        ConnectivityNotificationViewModel connectivityNotification)
    {
        _bottomSheetService = bottomSheetService;
        _storageProvider = storageProvider;


        WindowWidth = 480;
        WindowHeight = 720;
        CanResize = false;
        WindowTitle = string.Empty;

        TitleBarViewModel = new();

        LanguageSelector = new LanguageSelectorViewModel(
            localizationService,
            storageProvider,
            rpcMetaDataProvider);

        ConnectivityNotification = connectivityNotification;

        SetupHandlersAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[MAIN-WINDOW-VM] Unhandled exception in setup handlers");
                }
            },
            TaskScheduler.Default);
    }

    public async Task SetAuthenticationContentAsync(object content)
    {
        _isMainContentActive = false;
        await AnimateWindowResizeAsync(480, 720, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = false;
        TitleBarViewModel.DisableMaximizeButton = true;

        await SetContentWithFadeAsync(content).ConfigureAwait(false);
    }

    public async Task SetMainContentAsync(object content)
    {
        _isMainContentActive = true;
        await AnimateWindowResizeAsync(1200, 800, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = true;
        TitleBarViewModel.DisableMaximizeButton = false;

        await SetContentWithFadeAsync(content).ConfigureAwait(false);
    }

    public async Task ShowBottomSheetAsync(
        BottomSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false) =>
        await _bottomSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);

    public async Task HideBottomSheetAsync() => await _bottomSheetService.HideAsync().ConfigureAwait(false);

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) => _bottomSheetService.OnBottomSheetHidden(handler, lifetime);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        LanguageSelector.Dispose();
        ConnectivityNotification.Dispose();

        _isDisposed = true;
    }

    private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;


    private bool IsWindowSnapped()
    {
        if (GetPrimaryScreenWorkingArea == null)
        {
            return false;
        }

        Rect workingArea = GetPrimaryScreenWorkingArea();
        PixelPoint position = CurrentPosition;
        double width = WindowWidth;
        double height = WindowHeight;

        const int tolerance = 10;
        bool IsNear(double val1, double val2) => Math.Abs(val1 - val2) <= tolerance;

        bool leftAligned = IsNear(position.X, workingArea.Left);
        bool rightAligned = IsNear(position.X + width, workingArea.Right);
        bool topAligned = IsNear(position.Y, workingArea.Top);
        bool bottomAligned = IsNear(position.Y + height, workingArea.Bottom);

        bool isQuarterWidth = IsNear(width, workingArea.Width / 4);
        bool isThirdWidth = IsNear(width, workingArea.Width / 3);
        bool isTwoThirdsWidth = IsNear(width, workingArea.Width * 2 / 3);
        bool isHalfWidth = IsNear(width, workingArea.Width / 2);
        bool isFullWidth = IsNear(width, workingArea.Width);
        bool isHalfHeight = IsNear(height, workingArea.Height / 2);
        bool isFullHeight = IsNear(height, workingArea.Height);

        bool isSideAligned = leftAligned || rightAligned;
        bool isVerticalAligned = topAligned || bottomAligned;
        bool isFractionalWidth = isHalfWidth || isThirdWidth || isTwoThirdsWidth || isQuarterWidth;
        bool isCornerAligned = isSideAligned && isVerticalAligned;

        if (topAligned && isSideAligned && isFractionalWidth)
        {
            return true;
        }

        if (isCornerAligned && isHalfWidth && isHalfHeight)
        {
            return true;
        }

        if (topAligned && isFullWidth && isFullHeight)
        {
            return true;
        }

        if (isFullWidth && isHalfHeight && isVerticalAligned)
        {
            return true;
        }

        return false;
    }

    private static Task SetupHandlersAsync() => Task.CompletedTask;

    private async Task HandleWindowSnapAsync()
    {
        if (IsWindowSnapped())
        {
            PixelPoint currentPos = CurrentPosition;
            OnWindowRepositionRequested?.Invoke(new PixelPoint(currentPos.X + 1, currentPos.Y + 1));
            await Task.Delay(200);
        }
    }

    private async Task HandleFullScreenStateAsync()
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            await Task.Delay(500);
        }
    }

    private async Task AnimateWindowResizeAsync(double targetWidth, double targetHeight, TimeSpan duration)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await HandleFullScreenStateAsync();

            SyncViewModelWithActualWindowSize?.Invoke();

            await HandleWindowSnapAsync();
        });


        double startWidth = WindowWidth;
        double startHeight = WindowHeight;

        PixelPoint startPosition = CurrentPosition;
        PixelPoint? targetPosition = null;

        if (GetPrimaryScreenWorkingArea != null)
        {
            Rect workingArea = GetPrimaryScreenWorkingArea();

            double currentWindowCenterX = startPosition.X + startWidth / 2;
            double currentWindowCenterY = startPosition.Y + startHeight / 2;
            double targetX = currentWindowCenterX - targetWidth / 2;
            double targetY = currentWindowCenterY - targetHeight / 2;

            targetX = Math.Max(workingArea.X, Math.Min(targetX, workingArea.X + workingArea.Width - targetWidth));
            targetY = Math.Max(workingArea.Y, Math.Min(targetY, workingArea.Y + workingArea.Height - targetHeight));

            targetPosition = new PixelPoint((int)targetX, (int)targetY);
        }

        if (Math.Abs(startWidth - targetWidth) < 0.01 && Math.Abs(startHeight - targetHeight) < 0.01)
        {
            if (targetPosition.HasValue && startPosition != targetPosition)
            {
                OnWindowRepositionRequested?.Invoke(targetPosition.Value);
            }
            return;
        }

        const int steps = 30;
        TimeSpan stepDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps);

        for (int i = 1; i <= steps; i++)
        {
            double progress = (double)i / steps;
            double easedProgress = EaseInOutCubic(progress);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                WindowWidth = startWidth + (targetWidth - startWidth) * easedProgress;
                WindowHeight = startHeight + (targetHeight - startHeight) * easedProgress;


                if (targetPosition.HasValue)
                {
                    int currentX = (int)(startPosition.X + (targetPosition.Value.X - startPosition.X) * easedProgress);
                    int currentY = (int)(startPosition.Y + (targetPosition.Value.Y - startPosition.Y) * easedProgress);
                    OnWindowRepositionRequested?.Invoke(new PixelPoint(currentX, currentY));
                }
            });

            await Task.Delay(stepDuration).ConfigureAwait(true);
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            WindowWidth = targetWidth;
            WindowHeight = targetHeight;

            if (targetPosition.HasValue)
            {
                OnWindowRepositionRequested?.Invoke(targetPosition.Value);
            }
        });
    }

    public async Task<WindowPlacement?> LoadInitialPlacementAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _storageProvider.GetApplicationInstanceSettingsAsync();
        if (settingsResult.IsOk)
        {
            Log.Information("[MAIN-WINDOW-VM] Successfully loaded previous window state from secure storage");
            return settingsResult.Unwrap().WindowPlacement;
        }

        Log.Warning("[MAIN-WINDOW-VM] Cannot load the previous window state from secure storage: {Error}",
            settingsResult.UnwrapErr().Message);
        return null;
    }

    public async Task SavePlacementAsync(WindowState state, PixelPoint position, Size clientSize)
    {
        if (!_isMainContentActive)
        {
            Log.Information("[MAIN-WINDOW-VM] Пропуск збереження стану вікна (активний екран автентифікації).");
            return;
        }

        WindowPlacement? placement = (await LoadInitialPlacementAsync()) ?? new WindowPlacement();

        if (state == WindowState.Normal)
        {
            placement.PositionX = position.X;
            placement.PositionY = position.Y;
            placement.ClientWidth = clientSize.Width;
            placement.ClientHeight = clientSize.Height;
            Log.Information("[MAIN-WINDOW-VM] Saved window size {Width}x{Height} and position {X}:{Y}",
                clientSize.Width, clientSize.Height, position.X, position.Y);
        }

        placement.WindowState = (int)(state == WindowState.Minimized ? WindowState.Normal : state);

        Result<Unit, InternalServiceApiFailure> result = await _storageProvider.SetWindowPlacementAsync(placement);
        if (result.IsErr)
        {
            Log.Warning("[MAIN-WINDOW-VM] Cannot save window state: {Error}", result.UnwrapErr().Message);
            //TODO delete debug comments, keep only warging and error
        }
    }

    private async Task SetContentWithFadeAsync(object content)
    {
        if (CurrentContent != null)
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        CurrentContent = content;

        await Task.Delay(100).ConfigureAwait(false);
    }


}
