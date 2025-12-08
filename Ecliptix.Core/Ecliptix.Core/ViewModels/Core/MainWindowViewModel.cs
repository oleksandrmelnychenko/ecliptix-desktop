using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
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
using Ecliptix.Core.Views.Core.Configuration;
using Ecliptix.Core.Views.Core.Constants;
using Ecliptix.Core.Views.Core.Factories;
using Ecliptix.Core.Views.Core.Models;
using Ecliptix.Core.Views.Core.Services;
using Ecliptix.Core.Views.Memberships.Components;
using Ecliptix.Core.Views.Memberships.Components.TitleBar;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.ViewModels.Core;

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
    private readonly IWindowAnimationService _animationService;
    private readonly IWindowPositionService _positionService;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly MainWindowConfiguration _configuration;

    private readonly List<IDisposable> _trackedDisposables = new();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>> _titleBarCollectionCache;

    private volatile bool _isDisposed;
    private volatile bool _isMainContentActive;

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
        IWindowAnimationService animationService,
        IWindowPositionService positionService,
        IViewModelFactory viewModelFactory,
        MainWindowConfiguration configuration,
        ConnectivityNotificationViewModel connectivityNotification)
    {
        _sideSheetService = sideSheetService ?? throw new ArgumentNullException(nameof(sideSheetService));
        _bottomSheetService = bottomSheetService ?? throw new ArgumentNullException(nameof(bottomSheetService));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _rpcMetaDataProvider = rpcMetaDataProvider ?? throw new ArgumentNullException(nameof(rpcMetaDataProvider));
        _animationService = animationService ?? throw new ArgumentNullException(nameof(animationService));
        _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
        _viewModelFactory = viewModelFactory ?? throw new ArgumentNullException(nameof(viewModelFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        MinWindowWidth = MainWindowConstants.Dimensions.MIN_WINDOW_WIDTH;
        MinWindowHeight = MainWindowConstants.Dimensions.MIN_WINDOW_HEIGHT;

        WindowWidth = MainWindowConstants.Dimensions.DEFAULT_WINDOW_WIDTH;
        WindowHeight = MainWindowConstants.Dimensions.DEFAULT_WINDOW_HEIGHT;

        CanResize = false;
        WindowTitle = string.Empty;

        TitleBarViewModel = new TitleBarViewModel();

        LanguageSelector = new LanguageSelectorViewModel(
            localizationService,
            storageProvider,
            rpcMetaDataProvider);
        Track(LanguageSelector);

        ConnectivityNotification = connectivityNotification ?? throw new ArgumentNullException(nameof(connectivityNotification));
        Track(ConnectivityNotification);

        _titleBarCollectionCache = InitializeTitleBarCollectionCache();

        SetupHandlersAsync(_cancellationTokenSource.Token).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[MAIN-WINDOW-VM] Unhandled exception in setup handlers");
                }
            },
            TaskScheduler.Default);
    }

    private T Track<T>(T obj) where T : class
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        if (obj is IDisposable disposable)
        {
            lock (_trackedDisposables)
            {
                _trackedDisposables.Add(disposable);
            }
        }

        return obj;
    }

    private Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>> InitializeTitleBarCollectionCache()
    {
        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        return new Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>>
        {
            [TitleBarPosition.Left] = _ => TitleBarViewModel.LeftContent,
            [TitleBarPosition.Right] = _ => TitleBarViewModel.RightContent,
            [TitleBarPosition.Mirrored] = _ => isMac ? TitleBarViewModel.RightContent : TitleBarViewModel.LeftContent,
            [TitleBarPosition.ReverseMirrored] = _ => isMac ? TitleBarViewModel.LeftContent : TitleBarViewModel.RightContent
        };
    }

    public async Task SetAuthenticationContentAsync(object content, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationTokenSource.Token,
            cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TitleBarViewModel.IsDraggingEnabled = false;
        });

        await WaitUntilNotDragging(linkedCts.Token).ConfigureAwait(false);

        try
        {
            _isMainContentActive = false;

            await InvalidateWindowPlacementAsync(linkedCts.Token).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                CanResize = false;
                MinWindowWidth = 0;
                MinWindowHeight = 0;
            }, DispatcherPriority.Loaded);

            if (_configuration.EnableAnimations)
            {
                await AnimateWindowResizeAsync(
                    MainWindowConstants.Dimensions.AUTH_WINDOW_WIDTH,
                    MainWindowConstants.Dimensions.AUTH_WINDOW_HEIGHT,
                    MainWindowConstants.TimeSpans.AnimationDuration,
                    linkedCts.Token).ConfigureAwait(false);
            }

            VerticalSeparatorViewModel separator = Track(_viewModelFactory.Create<VerticalSeparatorViewModel>());
            LanguageSwitcherViewModel languageSwitcher = Track(_viewModelFactory.Create<LanguageSwitcherViewModel>(
                _sideSheetService,
                _storageProvider,
                _localizationService,
                _rpcMetaDataProvider));
            EppBadgeViewModel eppBadge = Track(_viewModelFactory.Create<EppBadgeViewModel>());
            NetworkBadgeViewModel networkBadge = Track(_viewModelFactory.Create<NetworkBadgeViewModel>());

            Dispatcher.UIThread.Post(() =>
            {
                ClearTitleBarContent();
                TitleBarViewModel.DisableMaximizeButton = true;

                SetMultipleTitleBarContent(
                    TitleBarPosition.ReverseMirrored,
                    reverseOrder: !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    clearOthers: false,
                    separator,
                    languageSwitcher);

                SetMultipleTitleBarContent(
                    TitleBarPosition.Mirrored,
                    reverseOrder: !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    clearOthers: false,
                    eppBadge,
                    networkBadge);
            });

            await SetContentWithFadeAsync(content, linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                TitleBarViewModel.IsDraggingEnabled = true;
            });
        }
    }

    public async Task SetMainContentAsync(object content, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            _cancellationTokenSource.Token,
            cancellationToken);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TitleBarViewModel.IsDraggingEnabled = false;
        });

        await WaitUntilNotDragging(linkedCts.Token).ConfigureAwait(false);

        try
        {
            _isMainContentActive = true;

            if (_configuration.EnableAnimations)
            {
                await AnimateWindowResizeAsync(
                    MainWindowConstants.Dimensions.MAIN_WINDOW_WIDTH,
                    MainWindowConstants.Dimensions.MAIN_WINDOW_HEIGHT,
                    MainWindowConstants.TimeSpans.AnimationDuration,
                    linkedCts.Token).ConfigureAwait(false);
            }

            Dispatcher.UIThread.Post(() =>
            {
                CanResize = true;
                MinWindowWidth = MainWindowConstants.Dimensions.MIN_MAIN_WINDOW_WIDTH;
                MinWindowHeight = MainWindowConstants.Dimensions.MIN_MAIN_WINDOW_HEIGHT;

                TitleBarViewModel.DisableMaximizeButton = false;
                ClearTitleBarContent();

                ToggleNavigationSideBarViewModel toggleNav = Track(_viewModelFactory.Create<ToggleNavigationSideBarViewModel>());
                ToggleThemeViewModel toggleTheme = Track(_viewModelFactory.Create<ToggleThemeViewModel>());

                TitleBarViewModel.LeftContent.Add(toggleNav);
                TitleBarViewModel.RightContent.Add(toggleTheme);

                if (!string.IsNullOrEmpty(_configuration.DefaultUserName) &&
                    !string.IsNullOrEmpty(_configuration.DefaultUserTag))
                {
                    PersonalTagViewModel tagVm = Track(_viewModelFactory.Create<PersonalTagViewModel>(
                        _configuration.DefaultUserName,
                        _configuration.DefaultUserTag));

                    TitleBarViewModel.CenterContent = tagVm;
                }
            }, DispatcherPriority.Loaded);

            await SetContentWithFadeAsync(content, linkedCts.Token).ConfigureAwait(false);
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
        ThrowIfDisposed();

        if (clearOthers)
        {
            ClearTitleBarContent();
        }

        if (items == null || items.Length == 0)
        {
            return;
        }

        if (!_titleBarCollectionCache.TryGetValue(position, out Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>? getCollection))
        {
            return;
        }

        System.Collections.ObjectModel.ObservableCollection<object> targetCollection = getCollection(reverseOrder);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearTitleBarContent()
    {
        TitleBarViewModel.LeftContent.Clear();
        TitleBarViewModel.CenterContent = null;
        TitleBarViewModel.RightContent.Clear();
    }

    private async Task WaitUntilNotDragging(CancellationToken cancellationToken)
    {
        if (!TitleBarViewModel.IsDragging)
        {
            return;
        }

        await TitleBarViewModel.WhenAnyValue(x => x.IsDragging)
            .Where(isDragging => !isDragging)
            .Take(1)
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private async Task AnimateWindowResizeAsync(
        double targetWidth,
        double targetHeight,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await HandleFullScreenStateAsync(cancellationToken).ConfigureAwait(false);
            SyncViewModelWithActualWindowSize?.Invoke();
            await HandleWindowSnapAsync(cancellationToken).ConfigureAwait(false);
        });

        double startWidth = WindowWidth;
        double startHeight = WindowHeight;
        PixelPoint startPosition = CurrentPosition;

        if (!_positionService.ValidateDimensions(targetWidth, targetHeight))
        {
            Log.Warning("[MAIN-WINDOW-VM] Invalid target dimensions: {Width}x{Height}", targetWidth, targetHeight);
            return;
        }

        PixelPoint? targetPosition = null;
        if (GetPrimaryScreenWorkingArea != null)
        {
            Rect workingArea = GetPrimaryScreenWorkingArea();
            targetPosition = _animationService.CalculateTargetPosition(
                startPosition,
                new Size(startWidth, startHeight),
                new Size(targetWidth, targetHeight),
                workingArea);
        }

        WindowAnimationState state = new(
            startWidth,
            startHeight,
            startPosition,
            targetWidth,
            targetHeight,
            targetPosition,
            DateTime.UtcNow,
            duration);

        if (!state.NeedsSizeChange && !state.NeedsPositionChange)
        {
            return;
        }

        await _animationService.AnimateWindowAsync(
            state,
            (progress, size, position) =>
            {
                WindowWidth = size.Width;
                WindowHeight = size.Height;

                if (position.HasValue)
                {
                    OnWindowRepositionRequested?.Invoke(position.Value);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleWindowSnapAsync(CancellationToken cancellationToken)
    {
        if (GetPrimaryScreenWorkingArea == null)
        {
            return;
        }

        Rect workingArea = GetPrimaryScreenWorkingArea();
        Size currentSize = new(WindowWidth, WindowHeight);

        if (_positionService.IsWindowSnapped(CurrentPosition, currentSize, workingArea))
        {
            PixelPoint currentPos = CurrentPosition;
            PixelPoint nudgedPosition = new(
                currentPos.X + MainWindowConstants.Layout.WINDOW_REPOSITION_OFFSET,
                currentPos.Y + MainWindowConstants.Layout.WINDOW_REPOSITION_OFFSET);

            OnWindowRepositionRequested?.Invoke(nudgedPosition);
            await Task.Delay(MainWindowConstants.TimeSpans.WindowSnapCheckDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleFullScreenStateAsync(CancellationToken cancellationToken)
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            await Task.Delay(MainWindowConstants.TimeSpans.FullScreenRestoreDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task SetContentWithFadeAsync(object content, CancellationToken cancellationToken)
    {
        if (CurrentContent != null)
        {
            await Task.Delay(MainWindowConstants.TimeSpans.FadeDelay, cancellationToken).ConfigureAwait(false);
        }

        CurrentContent = content;

        await Task.Delay(MainWindowConstants.TimeSpans.FadeDelay, cancellationToken).ConfigureAwait(false);
    }

    public async Task ShowBottomSheetAsync(
        BottomSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false,
        CancellationToken cancellationToken = default) =>
        await _bottomSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);

    public async Task ShowSideSheetAsync(
        SideSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false,
        CancellationToken cancellationToken = default) =>
        await _sideSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);

    public async Task HideBottomSheetAsync(CancellationToken cancellationToken = default) =>
        await _bottomSheetService.HideAsync().ConfigureAwait(false);

    public async Task HideSideSheetAsync(CancellationToken cancellationToken = default) =>
        await _sideSheetService.HideAsync().ConfigureAwait(false);

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _bottomSheetService.OnBottomSheetHidden(handler, lifetime);

    public IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _sideSheetService.OnSideSheetHidden(handler, lifetime);

    public async Task<WindowPlacement?> LoadInitialPlacementAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _storageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        return settingsResult.IsOk ? settingsResult.Unwrap().WindowPlacement : null;
    }

    private async Task InvalidateWindowPlacementAsync(CancellationToken cancellationToken)
    {
        if (!_configuration.SaveWindowPlacement)
        {
            return;
        }

        try
        {
            WindowPlacement placement = (await LoadInitialPlacementAsync(cancellationToken).ConfigureAwait(false)) ?? new WindowPlacement();

            placement.IsValidSave = false;

            Result<Unit, InternalServiceApiFailure> result =
                await _storageProvider.SetWindowPlacementAsync(placement).ConfigureAwait(false);

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

    public async Task SavePlacementAsync(
        WindowState state,
        PixelPoint position,
        Size clientSize,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_isMainContentActive || !_configuration.SaveWindowPlacement)
        {
            return;
        }

        WindowPlacement placement = (await LoadInitialPlacementAsync(cancellationToken).ConfigureAwait(false)) ?? new WindowPlacement();

        if (state == WindowState.Normal)
        {
            placement.PositionX = position.X;
            placement.PositionY = position.Y;
            placement.ClientWidth = clientSize.Width;
            placement.ClientHeight = clientSize.Height;
        }

        placement.WindowState = (int)(state == WindowState.Minimized ? WindowState.Normal : state);
        placement.IsValidSave = true;

        Result<Unit, InternalServiceApiFailure> result = await _storageProvider.SetWindowPlacementAsync(placement).ConfigureAwait(false);
        if (result.IsErr)
        {
            Log.Warning("[MAIN-WINDOW-VM] Cannot save window state: {Error}", result.UnwrapErr().Message);
        }
    }

    private static Task SetupHandlersAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(MainWindowViewModel));
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        OnWindowRepositionRequested = null;
        GetPrimaryScreenWorkingArea = null;
        SyncViewModelWithActualWindowSize = null;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();

        TitleBarViewModel.LeftContent.Clear();
        TitleBarViewModel.RightContent.Clear();
        TitleBarViewModel.CenterContent = null;

        lock (_trackedDisposables)
        {
            for (int i = _trackedDisposables.Count - 1; i >= 0; i--)
            {
                try
                {
                    _trackedDisposables[i]?.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[MAIN-WINDOW-VM] Error disposing tracked ViewModel at index {Index}", i);
                }
            }

            _trackedDisposables.Clear();
        }

        _viewModelFactory.DisposeAll();
    }
}
