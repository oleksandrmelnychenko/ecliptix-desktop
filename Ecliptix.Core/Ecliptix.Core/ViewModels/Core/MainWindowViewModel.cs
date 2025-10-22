using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.ViewModels.Core;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
    private readonly IBottomSheetService _bottomSheetService;
    private readonly ILocalizationService _localizationService;
    private readonly IApplicationSecureStorageProvider _storageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private readonly IMessageBus _messageBus;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public object? CurrentContent { get; private set; }

    [Reactive] public double WindowWidth { get; private set; }

    [Reactive] public double WindowHeight { get; private set; }

    [Reactive] public bool CanResize { get; private set; }

    [Reactive] public bool IsAuthenticated { get; private set; }

    [Reactive] public string WindowTitle { get; set; }

    public LanguageSelectorViewModel LanguageSelector { get; }

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }

    public MainWindowViewModel(
        IBottomSheetService bottomSheetService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider storageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        ConnectivityNotificationViewModel connectivityNotification,
        IMessageBus messageBus)
    {
        _bottomSheetService = bottomSheetService;
        _localizationService = localizationService;
        _storageProvider = storageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _messageBus = messageBus;

        WindowWidth = 480;
        WindowHeight = 720;
        CanResize = false;
        IsAuthenticated = false;
        WindowTitle = "Ecliptix";

        LanguageSelector = new LanguageSelectorViewModel(
            localizationService,
            storageProvider,
            rpcMetaDataProvider);

        ConnectivityNotification = connectivityNotification;

        Log.Information("[MainWindowViewModel] Initialized with dimensions: {Width}x{Height}", WindowWidth, WindowHeight);
        _ = SetupHandlersAsync();
    }

    private async Task SetupHandlersAsync()
    {
        try
        {
            Log.Information("[MainWindowViewModel] Message handlers setup completed");

            await Task.CompletedTask;

        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MainWindowViewModel] Error setting up message handlers");
        }
    }

    public async Task SetAuthenticationContentAsync(object content)
    {
        Log.Information("[MainWindowViewModel] Switching to authentication content");

        await AnimateWindowResizeAsync(480, 720, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = false;
        IsAuthenticated = false;

        await SetContentWithFadeAsync(content).ConfigureAwait(false);

        Log.Information("[MainWindowViewModel] Authentication content set");
    }

    public async Task SetMainContentAsync(object content)
    {
        Log.Information("[MainWindowViewModel] Switching to main content");

        await AnimateWindowResizeAsync(1200, 800, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = true;
        IsAuthenticated = true;

        await SetContentWithFadeAsync(content).ConfigureAwait(false);

        Log.Information("[MainWindowViewModel] Main content set");
    }

    public async Task ShowBottomSheetAsync(
        BottomSheetComponentType type,
        UserControl view,
        bool showScrim = true,
        bool isDismissable = false)
    {
        Log.Debug("[MainWindowViewModel] Showing bottom sheet: {Type}", type);
        await _bottomSheetService.ShowAsync(type, view, showScrim, isDismissable).ConfigureAwait(false);
    }

    public async Task HideBottomSheetAsync()
    {
        Log.Debug("[MainWindowViewModel] Hiding bottom sheet");
        await _bottomSheetService.HideAsync().ConfigureAwait(false);
    }

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime)
    {
        return _bottomSheetService.OnBottomSheetHidden(handler, lifetime);
    }

    private async Task AnimateWindowResizeAsync(double targetWidth, double targetHeight, TimeSpan duration)
    {
        double startWidth = WindowWidth;
        double startHeight = WindowHeight;

        if (Math.Abs(startWidth - targetWidth) < 0.01 && Math.Abs(startHeight - targetHeight) < 0.01)
        {
            return;
        }

        Log.Debug("[MainWindowViewModel] Animating window resize: {StartWidth}x{StartHeight} -> {TargetWidth}x{TargetHeight}",
            startWidth, startHeight, targetWidth, targetHeight);

        int steps = 30;
        TimeSpan stepDuration = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / steps);

        for (int i = 1; i <= steps; i++)
        {
            double progress = (double)i / steps;
            double easedProgress = EaseInOutCubic(progress);

            WindowWidth = startWidth + (targetWidth - startWidth) * easedProgress;
            WindowHeight = startHeight + (targetHeight - startHeight) * easedProgress;

            await Task.Delay(stepDuration).ConfigureAwait(false);
        }

        WindowWidth = targetWidth;
        WindowHeight = targetHeight;
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

    private static double EaseInOutCubic(double t)
    {
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Log.Debug("[MainWindowViewModel] Disposing");

        _disposables.Dispose();
        LanguageSelector.Dispose();
        ConnectivityNotification.Dispose();

        _isDisposed = true;
    }
}
