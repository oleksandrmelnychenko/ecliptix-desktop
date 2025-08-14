using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Services;
using Ecliptix.Core.Network.Services.Retry;
using Ecliptix.Core.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls;

public enum NetworkConnectionState
{
    NoInternet,
    ServerNotResponding
}

public sealed class NetworkStatusNotificationViewModel : ReactiveObject, IDisposable
{
    public ILocalizationService LocalizationService { get; }

    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statusUpdateSemaphore = new(1, 1);

    private NetworkStatusNotification? _view;
    private Ellipse? _statusEllipse;
    private Border? _mainBorder;

    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;

    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(250);

    private readonly ObservableAsPropertyHelper<bool> _isVisible;
    public bool IsVisible => _isVisible.Value;

    private readonly ObservableAsPropertyHelper<bool> _isAnimating;
    public bool IsAnimating => _isAnimating.Value;

    private readonly ObservableAsPropertyHelper<string> _statusText;
    public string StatusText => _statusText.Value;

    private readonly ObservableAsPropertyHelper<string> _statusDescription;
    public string StatusDescription => _statusDescription.Value;

    private readonly ObservableAsPropertyHelper<string> _statusIconSource;
    public string StatusIconSource => _statusIconSource.Value;

    private readonly ObservableAsPropertyHelper<bool> _showRetryButton;
    public bool ShowRetryButton => _showRetryButton.Value;

    private readonly ObservableAsPropertyHelper<NetworkConnectionState> _connectionState;
    public NetworkConnectionState ConnectionState => _connectionState.Value;

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }

    public void SetView(NetworkStatusNotification view)
    {
        _view = view;
        _statusEllipse = view.FindControl<Ellipse>("StatusDot");
        _mainBorder = view.FindControl<Border>("MainBorder");
        CreateAnimations();
    }

    public NetworkStatusNotificationViewModel(
        ILocalizationService localizationService,
        INetworkEvents networkEvents,
        IRetryStrategy retryStrategy,
        IPendingRequestManager pendingRequestManager)
    {
        LocalizationService = localizationService;
        IPendingRequestManager pendingRequestManager1 = pendingRequestManager;

        IObservable<NetworkStatusChangedEvent> networkStatusEvents = networkEvents.NetworkStatusChanged
            .DistinctUntilChanged(e => e.State)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        IObservable<NetworkConnectionState> connectionStateObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.RetriesExhausted or 
                NetworkStatus.DataCenterDisconnected or 
                NetworkStatus.ServerShutdown => NetworkConnectionState.ServerNotResponding,
                _ => NetworkConnectionState.NoInternet
            })
            .StartWith(NetworkConnectionState.NoInternet);

        IObservable<string> statusTextObservable = connectionStateObservable
            .Select(state => state switch
            {
                NetworkConnectionState.ServerNotResponding => LocalizationService["NetworkNotification.ServerNotResponding.Title"],
                _ => LocalizationService["NetworkNotification.NoInternet.Title"]
            });

        IObservable<string> statusDescriptionObservable = connectionStateObservable
            .Select(state => state switch
            {
                NetworkConnectionState.ServerNotResponding => LocalizationService["NetworkNotification.ServerNotResponding.Description"],
                _ => LocalizationService["NetworkNotification.NoInternet.Description"]
            });

        IObservable<string> statusIconObservable = connectionStateObservable
            .Select(state => state switch
            {
                NetworkConnectionState.ServerNotResponding => "avares://Ecliptix.Core/Assets/server-error.png",
                _ => "avares://Ecliptix.Core/Assets/wifi.png"
            });

        IObservable<bool> showRetryButtonObservable = networkStatusEvents
            .Select(evt => evt.State == NetworkStatus.RetriesExhausted)
            .StartWith(false);

        IObservable<bool> isVisibleObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.DataCenterConnected or NetworkStatus.ConnectionRestored => Observable.Return(true)
                    .Delay(TimeSpan.FromMilliseconds(2000))
                    .Select(_ => false),
                NetworkStatus.RetriesExhausted or 
                NetworkStatus.DataCenterDisconnected or 
                NetworkStatus.ServerShutdown => Observable.Return(true),
                _ => Observable.Return(false)
            })
            .Switch()
            .StartWith(false);

        _connectionState = connectionStateObservable.ToProperty(this, x => x.ConnectionState, scheduler: RxApp.MainThreadScheduler);
        _statusText = statusTextObservable.ToProperty(this, x => x.StatusText, scheduler: RxApp.MainThreadScheduler);
        _statusDescription = statusDescriptionObservable.ToProperty(this, x => x.StatusDescription, scheduler: RxApp.MainThreadScheduler);
        _statusIconSource = statusIconObservable.ToProperty(this, x => x.StatusIconSource, scheduler: RxApp.MainThreadScheduler);
        _showRetryButton = showRetryButtonObservable.ToProperty(this, x => x.ShowRetryButton, scheduler: RxApp.MainThreadScheduler);
        _isVisible = isVisibleObservable.ToProperty(this, x => x.IsVisible, scheduler: RxApp.MainThreadScheduler);
        _isAnimating = Observable.Return(false).ToProperty(this, x => x.IsAnimating, scheduler: RxApp.MainThreadScheduler);

        RetryCommand = ReactiveCommand.CreateFromTask(
            async ct => {
                try
                {
                    Log.Information("Manual retry requested - attempting to retry all pending requests");
                    int retriedCount = await pendingRequestManager1.RetryAllPendingRequestsAsync(ct);
                    Log.Information("Manual retry completed - retried {RetriedCount} pending requests", retriedCount);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during manual retry operation");
                }
            },
            showRetryButtonObservable
        );

        networkStatusEvents
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(evt => HandleNetworkStatusVisualEffects(evt))
            .DisposeWith(_disposables);

        isVisibleObservable
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async visible =>
            {
                if (visible)
                    await ShowAsync(CancellationToken.None);
                else
                    await HideAsync(CancellationToken.None);
            })
            .DisposeWith(_disposables);
    }

    private void HandleNetworkStatusVisualEffects(NetworkStatusChangedEvent evt)
    {
        bool isServerIssue = evt.State is NetworkStatus.RetriesExhausted or NetworkStatus.DataCenterDisconnected or NetworkStatus.ServerShutdown;
        ApplyClasses(serverIssue: isServerIssue);
    }

    private void ApplyClasses(bool serverIssue)
    {
        if (_mainBorder == null) return;

        if (serverIssue) _mainBorder.Classes.Add("CircuitOpen");
        else _mainBorder.Classes.Remove("CircuitOpen");
        
        _mainBorder.Classes.Remove("Retrying");
    }

    private void CreateAnimations()
    {
        if (_view == null) return;

        _appearAnimation = new Animation
        {
            Duration = AppearDuration,
            Easing = new QuadraticEaseOut(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, -20d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) }
                }
            }
        };

        _disappearAnimation = new Animation
        {
            Duration = DisappearDuration,
            Easing = new QuadraticEaseIn(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 0d), new Setter(TranslateTransform.YProperty, -15d) }
                }
            }
        };

    }

    private async Task ShowAsync(CancellationToken token)
    {
        if (_view == null) return;

        try
        {
            _view.RenderTransform = new TranslateTransform();
            if (_appearAnimation == null) CreateAnimations();
            await _appearAnimation!.RunAsync(_view, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error showing notification popup");
        }
    }

    private async Task HideAsync(CancellationToken token)
    {
        if (_view == null) return;

        try
        {
            if (_disappearAnimation == null) CreateAnimations();
            await _disappearAnimation!.RunAsync(_view, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error hiding notification popup");
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _statusUpdateSemaphore.Dispose();
    }
}