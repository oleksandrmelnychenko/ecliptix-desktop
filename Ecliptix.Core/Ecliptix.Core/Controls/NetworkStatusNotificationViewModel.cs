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
using Ecliptix.Core.Network.Services.Retry;
using Ecliptix.Core.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls;

public sealed class NetworkStatusNotificationViewModel : ReactiveObject, IDisposable
{
    public ILocalizationService LocalizationService { get; }

    private readonly IRetryStrategy _retryStrategy;
    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statusUpdateSemaphore = new(1, 1);

    private NetworkStatusNotification? _view;
    private Ellipse? _statusEllipse;
    private Border? _mainBorder;

    private Animation? _flickerAnimation;
    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;

    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FlickerDuration { get; set; } = TimeSpan.FromMilliseconds(1500);

    private readonly ObservableAsPropertyHelper<bool> _isVisible;
    public bool IsVisible => _isVisible.Value;

    private readonly ObservableAsPropertyHelper<bool> _isAnimating;
    public bool IsAnimating => _isAnimating.Value;

    private readonly ObservableAsPropertyHelper<string> _statusText;
    public string StatusText => _statusText.Value;

    private readonly ObservableAsPropertyHelper<string> _statusDescription;
    public string StatusDescription => _statusDescription.Value;

    private readonly ObservableAsPropertyHelper<bool> _showRetryMetrics;
    public bool ShowRetryMetrics => _showRetryMetrics.Value;

    private readonly ObservableAsPropertyHelper<int> _currentAttempt;
    public int CurrentAttempt => _currentAttempt.Value;

    private readonly ObservableAsPropertyHelper<int> _maxAttempts;
    public int MaxAttempts => _maxAttempts.Value;

    private readonly ObservableAsPropertyHelper<string> _successRate;
    public string SuccessRate => _successRate.Value;

    private readonly ObservableAsPropertyHelper<IBrush> _successRateColor;
    public IBrush SuccessRateColor => _successRateColor.Value;

    private readonly ObservableAsPropertyHelper<bool> _showCircuitStatus;
    public bool ShowCircuitStatus => _showCircuitStatus.Value;

    private readonly ObservableAsPropertyHelper<string> _circuitStatusText;
    public string CircuitStatusText => _circuitStatusText.Value;

    private readonly ObservableAsPropertyHelper<IBrush> _circuitStatusColor;
    public IBrush CircuitStatusColor => _circuitStatusColor.Value;

    private readonly ObservableAsPropertyHelper<bool> _isRetrying;
    public bool IsRetrying => _isRetrying.Value;

    private readonly ObservableAsPropertyHelper<bool> _canRetry;
    public bool CanRetry => _canRetry.Value;

    private readonly ObservableAsPropertyHelper<string> _retryButtonTooltip;
    public string RetryButtonTooltip => _retryButtonTooltip.Value;

    private readonly ObservableAsPropertyHelper<bool> _showRetryProgress;
    public bool ShowRetryProgress => _showRetryProgress.Value;

    private readonly ObservableAsPropertyHelper<double> _retryProgress;
    public double RetryProgress => _retryProgress.Value;

    private readonly ObservableAsPropertyHelper<bool> _showNextRetryCountdown;
    public bool ShowNextRetryCountdown => _showNextRetryCountdown.Value;

    private readonly ObservableAsPropertyHelper<string> _nextRetryText;
    public string NextRetryText => _nextRetryText.Value;

    public ReactiveCommand<Unit, Unit> RequestManualRetryCommand { get; }

    public void SetView(NetworkStatusNotification view)
    {
        _view = view;
        _statusEllipse = view.FindControl<Ellipse>("StatusEllipse");
        _mainBorder = view.FindControl<Border>("MainBorder");
        CreateAnimations();
    }

    public NetworkStatusNotificationViewModel(
        ILocalizationService localizationService,
        INetworkEvents networkEvents,
        IRetryStrategy retryStrategy)
    {
        LocalizationService = localizationService;
        _retryStrategy = retryStrategy;

        var networkStatusEvents = networkEvents.NetworkStatusChanged
            .DistinctUntilChanged(e => e.State)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        var statusTextObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.RetriesExhausted => "Connection Failed",
                NetworkStatus.DataCenterDisconnected or NetworkStatus.ServerShutdown => "Server Unavailable",
                NetworkStatus.DataCenterConnecting or NetworkStatus.RestoreSecrecyChannel or NetworkStatus.ConnectionRecovering => "Connecting...",
                NetworkStatus.DataCenterConnected or NetworkStatus.ConnectionRestored => "Connected",
                _ => "No Internet Connection"
            })
            .StartWith("No Internet Connection");

        var statusDescriptionObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.RetriesExhausted => "",
                NetworkStatus.DataCenterDisconnected or NetworkStatus.ServerShutdown => "Click retry to reconnect",
                NetworkStatus.DataCenterConnecting => "Establishing connection",
                NetworkStatus.RestoreSecrecyChannel => "Restoring previous session",
                NetworkStatus.ConnectionRecovering => "Recovering connection",
                NetworkStatus.DataCenterConnected or NetworkStatus.ConnectionRestored => "Connection restored",
                _ => "Check your connection"
            })
            .StartWith("Check your connection");

        var canRetryObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.DataCenterConnecting or NetworkStatus.RestoreSecrecyChannel or NetworkStatus.ConnectionRecovering => false,
                _ => true
            })
            .StartWith(true);

        var showRetryMetricsObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.RetriesExhausted or 
                NetworkStatus.DataCenterDisconnected or 
                NetworkStatus.ServerShutdown or
                NetworkStatus.DataCenterConnecting or 
                NetworkStatus.RestoreSecrecyChannel or 
                NetworkStatus.ConnectionRecovering => true,
                _ => false
            })
            .StartWith(false);

        var isRetryingObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.DataCenterConnecting or NetworkStatus.RestoreSecrecyChannel or NetworkStatus.ConnectionRecovering => true,
                _ => false
            })
            .StartWith(false);

        var isVisibleObservable = networkStatusEvents
            .Select(evt => evt.State switch
            {
                NetworkStatus.DataCenterConnected or NetworkStatus.ConnectionRestored => Observable.Return(true)
                    .Delay(TimeSpan.FromMilliseconds(2000))
                    .Select(_ => false),
                NetworkStatus.RetriesExhausted or 
                NetworkStatus.DataCenterDisconnected or 
                NetworkStatus.ServerShutdown or
                NetworkStatus.DataCenterConnecting or 
                NetworkStatus.RestoreSecrecyChannel or 
                NetworkStatus.ConnectionRecovering => Observable.Return(true),
                _ => Observable.Return(false)
            })
            .Switch()
            .StartWith(false);

        var retryMetricsObservable = Observable.Interval(TimeSpan.FromSeconds(1))
            .StartWith(0L)
            .Select(_ => _retryStrategy.GetRetryMetrics())
            .DistinctUntilChanged();

        var successRateObservable = retryMetricsObservable
            .Select(metrics => metrics.TotalAttempts > 0 
                ? $"{(double)metrics.SuccessfulAttempts / metrics.TotalAttempts * 100.0:F0}%" 
                : "0%");

        var successRateColorObservable = retryMetricsObservable
            .Select(metrics =>
            {
                if (metrics.TotalAttempts == 0) return new SolidColorBrush(Color.Parse("#808080"));
                double rate = (double)metrics.SuccessfulAttempts / metrics.TotalAttempts * 100.0;
                return rate switch
                {
                    >= 80 => new SolidColorBrush(Color.Parse("#84cd57")),
                    >= 50 => new SolidColorBrush(Color.Parse("#ffa500")),
                    _ => new SolidColorBrush(Color.Parse("#d81c1c"))
                };
            });

        _statusText = statusTextObservable.ToProperty(this, x => x.StatusText, scheduler: RxApp.MainThreadScheduler);
        _statusDescription = statusDescriptionObservable.ToProperty(this, x => x.StatusDescription, scheduler: RxApp.MainThreadScheduler);
        _canRetry = canRetryObservable.ToProperty(this, x => x.CanRetry, scheduler: RxApp.MainThreadScheduler);
        _showRetryMetrics = showRetryMetricsObservable.ToProperty(this, x => x.ShowRetryMetrics, scheduler: RxApp.MainThreadScheduler);
        _isRetrying = isRetryingObservable.ToProperty(this, x => x.IsRetrying, scheduler: RxApp.MainThreadScheduler);
        _isVisible = isVisibleObservable.ToProperty(this, x => x.IsVisible, scheduler: RxApp.MainThreadScheduler);
        _successRate = successRateObservable.ToProperty(this, x => x.SuccessRate, scheduler: RxApp.MainThreadScheduler);
        _successRateColor = successRateColorObservable.ToProperty(this, x => x.SuccessRateColor, scheduler: RxApp.MainThreadScheduler);

        _currentAttempt = Observable.Return(0).ToProperty(this, x => x.CurrentAttempt, scheduler: RxApp.MainThreadScheduler);
        _maxAttempts = Observable.Return(15).ToProperty(this, x => x.MaxAttempts, scheduler: RxApp.MainThreadScheduler);
        _showCircuitStatus = Observable.Return(false).ToProperty(this, x => x.ShowCircuitStatus, scheduler: RxApp.MainThreadScheduler);
        _circuitStatusText = Observable.Return("CLOSED").ToProperty(this, x => x.CircuitStatusText, scheduler: RxApp.MainThreadScheduler);
        _circuitStatusColor = Observable.Return<IBrush>(new SolidColorBrush(Color.Parse("#84cd57"))).ToProperty(this, x => x.CircuitStatusColor, scheduler: RxApp.MainThreadScheduler);
        _retryButtonTooltip = Observable.Return("Retry connection").ToProperty(this, x => x.RetryButtonTooltip, scheduler: RxApp.MainThreadScheduler);
        _showRetryProgress = Observable.Return(false).ToProperty(this, x => x.ShowRetryProgress, scheduler: RxApp.MainThreadScheduler);
        _retryProgress = Observable.Return(0.0).ToProperty(this, x => x.RetryProgress, scheduler: RxApp.MainThreadScheduler);
        _showNextRetryCountdown = Observable.Return(false).ToProperty(this, x => x.ShowNextRetryCountdown, scheduler: RxApp.MainThreadScheduler);
        _nextRetryText = Observable.Return("").ToProperty(this, x => x.NextRetryText, scheduler: RxApp.MainThreadScheduler);
        _isAnimating = Observable.Return(false).ToProperty(this, x => x.IsAnimating, scheduler: RxApp.MainThreadScheduler);

        RequestManualRetryCommand = ReactiveCommand.CreateFromTask(
            (CancellationToken ct) => Task.CompletedTask,
            this.WhenAnyValue(x => x.CanRetry, x => x.IsRetrying, (canRetry, isRetrying) => canRetry && !isRetrying)
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
        ApplyClasses(
            circuitOpen: evt.State == NetworkStatus.RetriesExhausted,
            retrying: evt.State is NetworkStatus.DataCenterConnecting or NetworkStatus.RestoreSecrecyChannel or NetworkStatus.ConnectionRecovering
        );
    }

    private void ApplyClasses(bool circuitOpen, bool retrying)
    {
        if (_mainBorder == null) return;

        if (circuitOpen) _mainBorder.Classes.Add("CircuitOpen");
        else _mainBorder.Classes.Remove("CircuitOpen");

        if (retrying) _mainBorder.Classes.Add("Retrying");
        else _mainBorder.Classes.Remove("Retrying");
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

        _flickerAnimation = new Animation
        {
            Duration = FlickerDuration,
            IterationCount = IterationCount.Infinite,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d),   Setters = { new Setter(Visual.OpacityProperty, 0.3d) } },
                new KeyFrame { Cue = new Cue(0.5d), Setters = { new Setter(Visual.OpacityProperty, 1d)   } },
                new KeyFrame { Cue = new Cue(1d),   Setters = { new Setter(Visual.OpacityProperty, 0.3d) } }
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
            StartFlickerAnimation();
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
            StopFlickerAnimation();
            if (_disappearAnimation == null) CreateAnimations();
            await _disappearAnimation!.RunAsync(_view, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error hiding notification popup");
        }
    }

    private void StartFlickerAnimation()
    {
        if (_flickerAnimation == null || _statusEllipse == null) CreateAnimations();
        _ = _flickerAnimation?.RunAsync(_statusEllipse!);
    }

    private void StopFlickerAnimation()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (_statusEllipse != null) _statusEllipse.Opacity = 1.0;
        }
        else
        {
            _ = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_statusEllipse != null) _statusEllipse.Opacity = 1.0;
            });
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _statusUpdateSemaphore.Dispose();
    }
}