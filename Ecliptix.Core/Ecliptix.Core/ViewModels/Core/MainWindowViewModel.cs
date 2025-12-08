using System;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
using Ecliptix.Core.Views.Memberships.Components.TitleBar;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.ViewModels.Core;

//TODO move out
public enum TitleBarPosition
{
    Left,
    Right,
    Mirrored,
    ReverseMirrored
}

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
    private readonly IBottomSheetService _bottomSheetService;
    private readonly ISideSheetService _sideSheetService;
    private readonly IApplicationSecureStorageProvider _storageProvider;
    private readonly ILocalizationService _localizationService;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;

    private bool _isDisposed;

    private bool _isMainContentActive;

    [Reactive] public WindowState WindowState { get; set; } = WindowState.Normal;
    [Reactive] public object? CurrentContent { get; private set; }

    [Reactive] public double WindowWidth { get; set; }
    [Reactive] public double WindowHeight { get; set; }

    [Reactive] public double MinWindowWidth { get; set; }
    [Reactive] public double MinWindowHeight { get; set; }

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
        ISideSheetService sideSheetService,
        IBottomSheetService bottomSheetService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider storageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        ConnectivityNotificationViewModel connectivityNotification)
    {
        _sideSheetService = sideSheetService;
        _bottomSheetService = bottomSheetService;
        _storageProvider = storageProvider;
        _localizationService = localizationService;
        _rpcMetaDataProvider = rpcMetaDataProvider;

        MinWindowWidth = 200;
        MinWindowHeight = 300;

        WindowWidth = 520;
        WindowHeight = 800;

        CanResize = false;
        WindowTitle = string.Empty;

        TitleBarViewModel = new TitleBarViewModel();

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
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TitleBarViewModel.IsDraggingEnabled = false;
        });

        await WaitUntilNotDragging().ConfigureAwait(false);

        try
        {
            _isMainContentActive = false;

            await InvalidateWindowPlacementAsync();

            Dispatcher.UIThread.Post(() =>
            {
                CanResize = false;
                MinWindowWidth = 0;
                MinWindowHeight = 0;
            }, DispatcherPriority.Loaded);


            await AnimateWindowResizeAsync(532, 812, TimeSpan.FromMilliseconds(450)).ConfigureAwait(false);

            VerticalSeparatorViewModel separator = new();
            LanguageSwitcherViewModel languageSwitcher = new(
                _sideSheetService,
                _storageProvider,
                _localizationService,
                _rpcMetaDataProvider
            );
            EppBadgeViewModel eppBadge = new();
            NetworkBadgeViewModel networkBadge = new();

            Dispatcher.UIThread.Post(() =>
            {
                ClearTitleBarContent();
                TitleBarViewModel.DisableMaximizeButton = true;

                SetMultipleTitleBarContent(
                    TitleBarPosition.ReverseMirrored,
                    reverseOrder: !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    clearOthers: false,
                    separator,
                    languageSwitcher
                );

                SetMultipleTitleBarContent(
                    TitleBarPosition.Mirrored,
                    reverseOrder: !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    clearOthers: false,
                    eppBadge,
                    networkBadge
                );
            });

            await SetContentWithFadeAsync(content).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TitleBarViewModel.IsDraggingEnabled = true;
            });
        }
    }

    public void SetMultipleTitleBarContent(
        TitleBarPosition position,
        bool reverseOrder,
        bool clearOthers,
        params object[] items)
    {
        if (clearOthers)
        {
            ClearTitleBarContent();
        }

        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        System.Collections.ObjectModel.ObservableCollection<object>? targetCollection = position switch
        {
            TitleBarPosition.Left => TitleBarViewModel.LeftContent,
            TitleBarPosition.Right => TitleBarViewModel.RightContent,
            TitleBarPosition.Mirrored => isMac ? TitleBarViewModel.RightContent : TitleBarViewModel.LeftContent,
            TitleBarPosition.ReverseMirrored => isMac ? TitleBarViewModel.LeftContent : TitleBarViewModel.RightContent,
            _ => null
        };

        if (targetCollection == null || items == null || items.Length == 0)
        {
            return;
        }

        if (reverseOrder)
        {
            for (int i = items.Length - 1; i >= 0; i--)
            {
                targetCollection.Add(items[i]);
            }
        }
        else
        {
            foreach (object item in items)
            {
                targetCollection.Add(item);
            }
        }
    }

    private void SetTitleBarContent(
        object content,
        TitleBarPosition position = TitleBarPosition.Mirrored,
        bool clearOthers = true)
    {
        if (clearOthers)
        {
            ClearTitleBarContent();
        }

        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        switch (position)
        {
            case TitleBarPosition.Left:
                TitleBarViewModel.LeftContent.Add(content);
                break;

            case TitleBarPosition.Right:
                TitleBarViewModel.RightContent.Add(content);
                break;

            case TitleBarPosition.Mirrored:
                if (isMac)
                {
                    TitleBarViewModel.RightContent.Add(content);
                }
                else
                {
                    TitleBarViewModel.LeftContent.Add(content);
                }

                break;

            case TitleBarPosition.ReverseMirrored:
                if (isMac)
                {
                    TitleBarViewModel.LeftContent.Add(content);
                }
                else
                {
                    TitleBarViewModel.RightContent.Add(content);
                }

                break;
        }
    }

    private void ClearTitleBarContent()
    {
        TitleBarViewModel.LeftContent.Clear();
        TitleBarViewModel.CenterContent = null;
        TitleBarViewModel.RightContent.Clear();
    }

    public async Task SetMainContentAsync(object content)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TitleBarViewModel.IsDraggingEnabled = false;
        });

        await WaitUntilNotDragging().ConfigureAwait(false);

        try
        {
            _isMainContentActive = true;

            await AnimateWindowResizeAsync(1200, 800, TimeSpan.FromMilliseconds(450)).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                CanResize = true;
                MinWindowWidth = 800;
                MinWindowHeight = 600;

                TitleBarViewModel.DisableMaximizeButton = false;
                ClearTitleBarContent();

                TitleBarViewModel.LeftContent.Add(new ToggleNavigationSideBarViewModel());
                TitleBarViewModel.RightContent.Add(new ToggleThemeViewModel());

                PersonalTagViewModel tagVm = new(
                    "Ecliptix",
                    "@oleksandr.melnychenko"
                );

                TitleBarViewModel.CenterContent = tagVm;
            }, DispatcherPriority.Loaded);

            await SetContentWithFadeAsync(content).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TitleBarViewModel.IsDraggingEnabled = true;
            });
        }
    }


    private async Task WaitUntilNotDragging()
    {
        if (!TitleBarViewModel.IsDragging)
        {
            return;
        }

        await TitleBarViewModel.WhenAnyValue(x => x.IsDragging)
            .Where(isDragging => !isDragging)
            .Take(1)
            .ToTask();
    }

    public async Task ShowBottomSheetAsync(
        BottomSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false) =>
        await _bottomSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);

    public async Task ShowSideSheetAsync(
        SideSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false) =>
        await _sideSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);

    public async Task HideBottomSheetAsync() => await _bottomSheetService.HideAsync().ConfigureAwait(false);

    public async Task HideSideSheetAsync() => await _sideSheetService.HideAsync().ConfigureAwait(false);

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _bottomSheetService.OnBottomSheetHidden(handler, lifetime);

    public IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _sideSheetService.OnSideSheetHidden(handler, lifetime);

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

        bool IsNear(double val1, double val2) => Math.Abs(val1 - val2) <= tolerance;
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
        if (WindowState is WindowState.Maximized or WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            await Task.Delay(500);
        }
    }

    private async Task AnimateWindowResizeAsync(double targetWidth, double targetHeight, TimeSpan duration)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await HandleFullScreenStateAsync();
            SyncViewModelWithActualWindowSize?.Invoke();
            await HandleWindowSnapAsync();
        });

        double startWidth = 0;
        double startHeight = 0;
        PixelPoint startPosition = new(0, 0);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            startWidth = WindowWidth;
            startHeight = WindowHeight;
            startPosition = CurrentPosition;
        });

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
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (targetPosition.HasValue && startPosition != targetPosition)
                {
                    OnWindowRepositionRequested?.Invoke(targetPosition.Value);
                }
            });
            return;
        }

        TaskCompletionSource tcs = new();
        DateTime startTime = DateTime.UtcNow;
        double totalDurationMs = duration.TotalMilliseconds;

        DispatcherTimer timer = new(
            TimeSpan.Zero,
            DispatcherPriority.Render,
            (sender, e) =>
            {
                DateTime now = DateTime.UtcNow;
                double elapsedMs = (now - startTime).TotalMilliseconds;

                double progress = Math.Min(1.0, elapsedMs / totalDurationMs);
                double easedProgress = EaseInOutCubic(progress);

                WindowWidth = startWidth + (targetWidth - startWidth) * easedProgress;
                WindowHeight = startHeight + (targetHeight - startHeight) * easedProgress;

                if (targetPosition.HasValue)
                {
                    int currentX = (int)(startPosition.X + (targetPosition.Value.X - startPosition.X) * easedProgress);
                    int currentY = (int)(startPosition.Y + (targetPosition.Value.Y - startPosition.Y) * easedProgress);
                    OnWindowRepositionRequested?.Invoke(new PixelPoint(currentX, currentY));
                }

                if (progress >= 1.0)
                {
                    (sender as DispatcherTimer)?.Stop();

                    WindowWidth = targetWidth;
                    WindowHeight = targetHeight;

                    if (targetPosition.HasValue)
                    {
                        OnWindowRepositionRequested?.Invoke(targetPosition.Value);
                    }

                    tcs.TrySetResult();
                }
            });

        timer.Start();

        await tcs.Task;
    }

    public async Task<WindowPlacement?> LoadInitialPlacementAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _storageProvider.GetApplicationInstanceSettingsAsync();
        return settingsResult.IsOk ? settingsResult.Unwrap().WindowPlacement : null;
    }

    private async Task InvalidateWindowPlacementAsync()
    {
        try
        {
            WindowPlacement placement = (await LoadInitialPlacementAsync()) ?? new WindowPlacement();

            placement.IsValidSave = false;

            Result<Unit, InternalServiceApiFailure> result =
                await _storageProvider.SetWindowPlacementAsync(placement);

            if (result.IsErr)
            {
                Log.Warning("[MAIN-WINDOW-VM] Cannot invalidate the window state: {Error}", result.UnwrapErr().Message);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MAIN-WINDOW-VM] Error during invalidation of the placement");
        }
    }

    public async Task SavePlacementAsync(WindowState state, PixelPoint position, Size clientSize)
    {
        if (!_isMainContentActive)
        {
            return;
        }

        WindowPlacement? placement = (await LoadInitialPlacementAsync()) ?? new WindowPlacement();

        if (state == WindowState.Normal)
        {
            placement.PositionX = position.X;
            placement.PositionY = position.Y;
            placement.ClientWidth = clientSize.Width;
            placement.ClientHeight = clientSize.Height;
        }

        placement.WindowState = (int)(state == WindowState.Minimized ? WindowState.Normal : state);
        placement.IsValidSave = true;

        Result<Unit, InternalServiceApiFailure> result = await _storageProvider.SetWindowPlacementAsync(placement);
        if (result.IsErr)
        {
            Log.Warning("[MAIN-WINDOW-VM] Cannot save window state: {Error}", result.UnwrapErr().Message);
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
