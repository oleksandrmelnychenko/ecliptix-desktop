using System;
using System.Threading.Tasks;

using Avalonia.Controls;

using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Serilog;

namespace Ecliptix.Core.ViewModels.Core;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
    private readonly IBottomSheetService _bottomSheetService;
    private bool _isDisposed;

    [Reactive] public object? CurrentContent { get; private set; }

    [Reactive] public double WindowWidth { get; private set; }

    [Reactive] public double WindowHeight { get; private set; }

    [Reactive] public bool CanResize { get; private set; }

    [Reactive] public string WindowTitle { get; set; }

    public LanguageSelectorViewModel LanguageSelector { get; }

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }

    public MainWindowViewModel(
        IBottomSheetService bottomSheetService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider storageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        ConnectivityNotificationViewModel connectivityNotification)
    {
        _bottomSheetService = bottomSheetService;

        WindowWidth = 520;
        WindowHeight = 800;
        CanResize = false;
        WindowTitle = string.Empty;

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
        await AnimateWindowResizeAsync(520, 800, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = false;

        await SetContentWithFadeAsync(content).ConfigureAwait(false);
    }

    public async Task SetMainContentAsync(object content)
    {
        await AnimateWindowResizeAsync(1200, 800, TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);

        CanResize = true;

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

    private static Task SetupHandlersAsync() => Task.CompletedTask;

    private async Task AnimateWindowResizeAsync(double targetWidth, double targetHeight, TimeSpan duration)
    {
        double startWidth = WindowWidth;
        double startHeight = WindowHeight;

        if (Math.Abs(startWidth - targetWidth) < 0.01 && Math.Abs(startHeight - targetHeight) < 0.01)
        {
            return;
        }

        const int steps = 30;
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
}
