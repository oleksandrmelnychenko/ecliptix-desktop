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
using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Events;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Controls.TitleBar;
using Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;
using Ecliptix.Core.Modularity.Suggestions;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Configuration;
using Ecliptix.Core.Shell.Constants;
using Ecliptix.Core.Shell.Factories;
using Ecliptix.Core.Shell.Models;
using Ecliptix.Core.Shell.Services;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.Shell.ViewModels;

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

    private readonly
        Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>>
        _titleBarCollectionCache;

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

    public LanguageCycleButtonViewModel LanguageSelector { get; }

    public TitleBarViewModel TitleBarViewModel { get; }
    public ISuggestionsViewModel SuggestionsViewModel { get; }
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
        _sideSheetService = sideSheetService;
        _bottomSheetService = bottomSheetService;
        _storageProvider = storageProvider;
        _localizationService = localizationService;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _animationService = animationService;
        _positionService = positionService;
        _viewModelFactory = viewModelFactory;
        _configuration = configuration;

        MinWindowWidth = MainWindowConstants.Dimensions.MIN_WINDOW_WIDTH;
        MinWindowHeight = MainWindowConstants.Dimensions.MIN_WINDOW_HEIGHT;

        WindowWidth = MainWindowConstants.Dimensions.DEFAULT_WINDOW_WIDTH;
        WindowHeight = MainWindowConstants.Dimensions.DEFAULT_WINDOW_HEIGHT;

        CanResize = false;
        WindowTitle = string.Empty;

        TitleBarViewModel = new TitleBarViewModel();

        SuggestionsViewModel = Track(_viewModelFactory.Create<ISuggestionsViewModel>());

        LanguageSelector = new LanguageCycleButtonViewModel(
            localizationService,
            storageProvider,
            rpcMetaDataProvider);
        Track(LanguageSelector);

        ConnectivityNotification = connectivityNotification;
        Track(ConnectivityNotification);

        _titleBarCollectionCache = InitializeTitleBarCollectionCache();

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

    private T Track<T>(T obj) where T : class
    {
        switch (obj)
        {
            case null:
                throw new ArgumentNullException(nameof(obj));
            case IDisposable disposable:
                {
                    lock (_trackedDisposables)
                    {
                        _trackedDisposables.Add(disposable);
                    }

                    break;
                }
        }

        return obj;
    }

    private Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>>
        InitializeTitleBarCollectionCache()
    {
        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        return new Dictionary<TitleBarPosition, Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>>
        {
            [TitleBarPosition.LEFT] = _ => TitleBarViewModel.LeftContent,
            [TitleBarPosition.RIGHT] = _ => TitleBarViewModel.RightContent,
            [TitleBarPosition.MIRRORED] =
                _ => isMac ? TitleBarViewModel.RightContent : TitleBarViewModel.LeftContent,
            [TitleBarPosition.REVERSE_MIRRORED] =
                _ => isMac ? TitleBarViewModel.LeftContent : TitleBarViewModel.RightContent
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

            await Dispatcher.UIThread.InvokeAsync(() => CurrentContent = null);

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
            LanguageMenuButtonViewModel languageMenuButton = Track(_viewModelFactory.Create<LanguageMenuButtonViewModel>(
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
                    TitleBarPosition.REVERSE_MIRRORED,
                    reverseOrder: !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    clearOthers: false,
                    separator,
                    languageMenuButton);

                SetMultipleTitleBarContent(
                    TitleBarPosition.MIRRORED,
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

            await Dispatcher.UIThread.InvokeAsync(() => CurrentContent = null);

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

                ToggleNavigationSideBarViewModel toggleNav =
                    Track(_viewModelFactory.Create<ToggleNavigationSideBarViewModel>());
                ToggleThemeViewModel toggleTheme = Track(_viewModelFactory.Create<ToggleThemeViewModel>());

                TitleBarViewModel.LeftContent.Add(toggleNav);
                TitleBarViewModel.RightContent.Add(toggleTheme);

                if (string.IsNullOrEmpty(_configuration.DefaultUserName) ||
                    string.IsNullOrEmpty(_configuration.DefaultUserTag))
                {
                    return;
                }

                PersonalTagViewModel tagVm = Track(_viewModelFactory.Create<PersonalTagViewModel>(
                    _configuration.DefaultUserName,
                    _configuration.DefaultUserTag));

                TitleBarViewModel.CenterContent = tagVm;
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

    private void SetMultipleTitleBarContent(
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

        if (items.Length == 0)
        {
            return;
        }

        if (!_titleBarCollectionCache.TryGetValue(position,
                out Func<bool, System.Collections.ObjectModel.ObservableCollection<object>>? getCollection))
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

        if (state is { NeedsSizeChange: false, NeedsPositionChange: false })
        {
            return;
        }

        await _animationService.AnimateWindowAsync(
            state,
            (_, size, position) =>
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
            await Task.Delay(MainWindowConstants.TimeSpans.WindowSnapCheckDelay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task HandleFullScreenStateAsync(CancellationToken cancellationToken)
    {
        if (WindowState is WindowState.Maximized or WindowState.FullScreen)
        {
            WindowState = WindowState.Normal;
            await Task.Delay(MainWindowConstants.TimeSpans.FullScreenRestoreDelay, cancellationToken)
                .ConfigureAwait(false);
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
        object viewModel,
        bool showScrim = true,
        bool isDismissable = false) =>
        await _bottomSheetService.ShowAsync(viewModel, showScrim, isDismissable).ConfigureAwait(false);

    public async Task ShowSideSheetAsync(
        object viewModel,
        bool showScrim = true,
        bool isDismissable = false) =>
        await _sideSheetService.ShowAsync(viewModel, showScrim, isDismissable).ConfigureAwait(false);

    public async Task HideBottomSheetAsync() =>
        await _bottomSheetService.HideAsync().ConfigureAwait(false);

    public async Task HideSideSheetAsync() =>
        await _sideSheetService.HideAsync().ConfigureAwait(false);

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _bottomSheetService.OnBottomSheetHidden(handler, lifetime);

    public IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime) =>
        _sideSheetService.OnSideSheetHidden(handler, lifetime);

    public async Task<WindowPlacement?> LoadInitialPlacementAsync()
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
            WindowPlacement placement =
                await LoadInitialPlacementAsync().ConfigureAwait(false) ?? new WindowPlacement();

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

        WindowPlacement placement = await LoadInitialPlacementAsync().ConfigureAwait(false) ?? new WindowPlacement();

        if (state == WindowState.Normal)
        {
            placement.PositionX = position.X;
            placement.PositionY = position.Y;
            placement.ClientWidth = (int)clientSize.Width;
            placement.ClientHeight = (int)clientSize.Height;
        }

        placement.WindowState = (int)(state == WindowState.Minimized ? WindowState.Normal : state);
        placement.IsValidSave = true;

        Result<Unit, InternalServiceApiFailure> result =
            await _storageProvider.SetWindowPlacementAsync(placement).ConfigureAwait(false);
        if (result.IsErr)
        {
            Log.Warning("[MAIN-WINDOW-VM] Cannot save window state: {Error}", result.UnwrapErr().Message);
        }
    }

    private static Task SetupHandlersAsync() => Task.CompletedTask;

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

        TitleBarViewModel.Dispose();

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
