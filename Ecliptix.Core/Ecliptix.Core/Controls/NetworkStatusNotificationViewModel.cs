using System;
using System.Linq;
using System.Reactive;
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
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services;
using Ecliptix.Core.Network.Services.Retry;
using Ecliptix.Core.Services;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Core.Controls;

public sealed class NetworkStatusNotificationViewModel : ReactiveObject, IDisposable
{
    public ILocalizationService LocalizationService { get; }

    private readonly INetworkEvents _networkEvents;
    private readonly IRetryStrategy _retryStrategy;
    private readonly NetworkProvider _networkProvider;
    private readonly IPendingRequestManager _pendingRequestManager;

    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statusUpdateSemaphore = new(1, 1);

    private NetworkStatusNotification? _view;
    private Ellipse? _statusEllipse;
    private Border? _mainBorder;

    private CancellationTokenSource? _retryCts;           // cancel manual retry
    private CancellationTokenSource? _uiTransitionCts;    // cancel show/hide + delays

    // Animations
    private Animation? _flickerAnimation;
    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;

    // Animation durations
    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FlickerDuration { get; set; } = TimeSpan.FromMilliseconds(1500);

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    private bool _isAnimating;
    public bool IsAnimating
    {
        get => _isAnimating;
        set => this.RaiseAndSetIfChanged(ref _isAnimating, value);
    }

    private string _statusText = "No Internet Connection";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private string _statusDescription = "Check your connection";
    public string StatusDescription
    {
        get => _statusDescription;
        set => this.RaiseAndSetIfChanged(ref _statusDescription, value);
    }

    private bool _showRetryMetrics;
    public bool ShowRetryMetrics
    {
        get => _showRetryMetrics;
        set => this.RaiseAndSetIfChanged(ref _showRetryMetrics, value);
    }

    private int _currentAttempt;
    public int CurrentAttempt
    {
        get => _currentAttempt;
        set => this.RaiseAndSetIfChanged(ref _currentAttempt, value);
    }

    private int _maxAttempts = 15;
    public int MaxAttempts
    {
        get => _maxAttempts;
        set => this.RaiseAndSetIfChanged(ref _maxAttempts, value);
    }

    private string _successRate = "0%";
    public string SuccessRate
    {
        get => _successRate;
        set => this.RaiseAndSetIfChanged(ref _successRate, value);
    }

    private IBrush _successRateColor = new SolidColorBrush(Color.Parse("#808080"));
    public IBrush SuccessRateColor
    {
        get => _successRateColor;
        set => this.RaiseAndSetIfChanged(ref _successRateColor, value);
    }

    private bool _showCircuitStatus;
    public bool ShowCircuitStatus
    {
        get => _showCircuitStatus;
        set => this.RaiseAndSetIfChanged(ref _showCircuitStatus, value);
    }

    private string _circuitStatusText = "CLOSED";
    public string CircuitStatusText
    {
        get => _circuitStatusText;
        set => this.RaiseAndSetIfChanged(ref _circuitStatusText, value);
    }

    private IBrush _circuitStatusColor = new SolidColorBrush(Color.Parse("#84cd57"));
    public IBrush CircuitStatusColor
    {
        get => _circuitStatusColor;
        set => this.RaiseAndSetIfChanged(ref _circuitStatusColor, value);
    }

    private bool _isRetrying;
    public bool IsRetrying
    {
        get => _isRetrying;
        set => this.RaiseAndSetIfChanged(ref _isRetrying, value);
    }

    private bool _canRetry = true;
    public bool CanRetry
    {
        get => _canRetry;
        set => this.RaiseAndSetIfChanged(ref _canRetry, value);
    }

    private string _retryButtonTooltip = "Retry connection";
    public string RetryButtonTooltip
    {
        get => _retryButtonTooltip;
        set => this.RaiseAndSetIfChanged(ref _retryButtonTooltip, value);
    }

    private bool _showRetryProgress;
    public bool ShowRetryProgress
    {
        get => _showRetryProgress;
        set => this.RaiseAndSetIfChanged(ref _showRetryProgress, value);
    }

    private double _retryProgress;
    public double RetryProgress
    {
        get => _retryProgress;
        set => this.RaiseAndSetIfChanged(ref _retryProgress, value);
    }

    private bool _showNextRetryCountdown;
    public bool ShowNextRetryCountdown
    {
        get => _showNextRetryCountdown;
        set => this.RaiseAndSetIfChanged(ref _showNextRetryCountdown, value);
    }

    private string _nextRetryText = "";
    public string NextRetryText
    {
        get => _nextRetryText;
        set => this.RaiseAndSetIfChanged(ref _nextRetryText, value);
    }

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
        IRetryStrategy retryStrategy,
        NetworkProvider networkProvider,
        IPendingRequestManager pendingRequestManager)
    {
        LocalizationService = localizationService;
        _networkEvents = networkEvents;
        _retryStrategy = retryStrategy;
        _networkProvider = networkProvider;
        _pendingRequestManager = pendingRequestManager;

        _networkEvents.NetworkStatusChanged
            .DistinctUntilChanged(e => e.State)                      
            .ObserveOn(RxApp.MainThreadScheduler)                   
            .Select(evt => Observable.FromAsync(ct => HandleNetworkStatusChange(evt, ct)))
            .Switch()                                              
            .Subscribe(_ => { }, ex => Log.Warning(ex, "Network status stream failed"))
            .DisposeWith(_disposables);

        RequestManualRetryCommand = ReactiveCommand.CreateFromTask(
            (CancellationToken ct) => RetryAllOperationsAsync(ct),
            this.WhenAnyValue(x => x.CanRetry, x => x.IsRetrying, (canRetry, isRetrying) => canRetry && !isRetrying)
        );
    }

    private async Task HandleNetworkStatusChange(NetworkStatusChangedEvent evt, CancellationToken token)
    {
        // cancel any pending UI transition or delay and create a fresh token
        _uiTransitionCts?.Cancel();
        _uiTransitionCts?.Dispose();
        _uiTransitionCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        var uiToken = _uiTransitionCts.Token;

        await _statusUpdateSemaphore.WaitAsync(uiToken);
        try
        {
            switch (evt.State)
            {
                case NetworkStatus.RetriesExhausted:
                    StatusText = "Connection Failed";
                    StatusDescription = $"{_pendingRequestManager.PendingRequestCount} requests pending";
                    ShowRetryMetrics = true;
                    UpdateRetryMetrics();
                    CanRetry = true;
                    ApplyClasses(circuitOpen: true, retrying: false);

                    if (!IsVisible)
                        await ShowAsync(uiToken);
                    break;

                case NetworkStatus.DataCenterDisconnected:
                case NetworkStatus.ServerShutdown:
                    StatusText = "Server Unavailable";
                    StatusDescription = "Click retry to reconnect";
                    ShowRetryMetrics = true;
                    CanRetry = true;
                    ApplyClasses(circuitOpen: false, retrying: false);

                    if (!IsVisible)
                        await ShowAsync(uiToken);
                    break;

                case NetworkStatus.DataCenterConnected:
                case NetworkStatus.ConnectionRestored:
                    StatusText = "Connected";
                    StatusDescription = "Connection restored";

                    try { await RetryPendingRequestsAsync(uiToken); }
                    catch (OperationCanceledException) { /* ignore */ }

                    if (IsVisible)
                    {
                        await DelaySafe(TimeSpan.FromMilliseconds(2000), uiToken);
                        await HideAsync(uiToken);
                    }
                    break;

                default:
                    // unknown states: keep silent but update metrics
                    UpdateRetryMetrics();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Newer event arrived or disposal – safely ignore
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error handling network status change: {StatusEvent}", evt.State);
        }
        finally
        {
            _statusUpdateSemaphore.Release();
        }
    }

    private void ApplyClasses(bool circuitOpen, bool retrying)
    {
        if (_mainBorder == null) return;

        if (circuitOpen) _mainBorder.Classes.Add("CircuitOpen");
        else _mainBorder.Classes.Remove("CircuitOpen");

        if (retrying) _mainBorder.Classes.Add("Retrying");
        else _mainBorder.Classes.Remove("Retrying");
    }

    private void UpdateRetryMetrics()
    {
        var metrics = _retryStrategy.GetRetryMetrics();
        if (metrics.TotalAttempts > 0)
        {
            var rate = (double)metrics.SuccessfulAttempts / metrics.TotalAttempts * 100.0;
            SuccessRate = $"{rate:F0}%";
            SuccessRateColor = rate switch
            {
                >= 80 => new SolidColorBrush(Color.Parse("#84cd57")),
                >= 50 => new SolidColorBrush(Color.Parse("#ffa500")),
                _ => new SolidColorBrush(Color.Parse("#d81c1c"))
            };
        }
        else
        {
            SuccessRate = "0%";
            SuccessRateColor = new SolidColorBrush(Color.Parse("#808080"));
        }
    }

    private async Task<int> RetryPendingRequestsAsync(CancellationToken token)
    {
        try
        {
            var count = await _pendingRequestManager.RetryAllPendingRequestsAsync(token);
            Log.Information("Retried {Count} pending requests", count);
            return count;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrying pending requests");
            return 0;
        }
    }

    private async Task RetryAllOperationsAsync(CancellationToken commandToken = default)
    {
        // Cancel any existing retry flow
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        _retryCts = CancellationTokenSource.CreateLinkedTokenSource(commandToken);
        var token = _retryCts.Token;

        try
        {
            Log.Information("User initiated manual retry with SecrecyChannel restoration");

            IsRetrying = true;
            CanRetry = false;
            CurrentAttempt = 0;
            StatusText = "Restoring Connection";
            StatusDescription = "Establishing secure channel...";

            // Reset retry/circuit state to start fresh
            _retryStrategy.ResetConnectionState();

            // Use provider’s built-in recovery path (no hard-coded connectId)
            var result = await _networkProvider.ForceFreshConnectionAsync();
            if (result.IsErr)
            {
                StatusText = "Connection Failed";
                StatusDescription = $"Failed to restore connection: {result.UnwrapErr().Message}";
                Log.Error("ForceFreshConnectionAsync failed: {Error}", result.UnwrapErr().Message);
                return;
            }

            StatusText = "Connection Restored";
            StatusDescription = "Retrying pending requests...";

            var successCount = await RetryPendingRequestsAsync(token);
            var remaining = _pendingRequestManager.PendingRequestCount;

            if (successCount > 0 && remaining == 0)
            {
                StatusText = "All Requests Successful";
                StatusDescription = $"{successCount} requests completed successfully";
                await DelaySafe(TimeSpan.FromMilliseconds(3000), token);
                await HideAsync(token);
            }
            else if (successCount > 0)
            {
                StatusText = "Partial Success";
                StatusDescription = $"{successCount} succeeded, {remaining} still pending";
            }
            else
            {
                StatusText = "Connection Restored";
                StatusDescription = "Ready to process new requests";
                await DelaySafe(TimeSpan.FromMilliseconds(2000), token);
                await HideAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // user initiated another retry or VM disposed – ignore
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during manual retry");
            StatusText = "Retry Error";
            StatusDescription = "An error occurred during retry";
        }
        finally
        {
            IsRetrying = false;
            // CRITICAL FIX: Always allow retry if there are pending requests OR if connection failed
            // This ensures consistent button behavior between first and subsequent clicks
            CanRetry = _pendingRequestManager.PendingRequestCount > 0;
            UpdateRetryMetrics();
        }
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
        if (_view == null || IsAnimating) return;

        IsAnimating = true;
        try
        {
            _view.RenderTransform = new TranslateTransform();
            IsVisible = true;

            if (_appearAnimation == null) CreateAnimations();

            await _appearAnimation!.RunAsync(_view, token);

            StartFlickerAnimation();
        }
        catch (OperationCanceledException)
        {
            IsVisible = false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error showing notification popup");
            IsVisible = false;
        }
        finally
        {
            IsAnimating = false;
        }
    }

    private async Task HideAsync(CancellationToken token)
    {
        if (_view == null || IsAnimating) return;

        IsAnimating = true;
        try
        {
            StopFlickerAnimation();

            if (_disappearAnimation == null) CreateAnimations();

            await _disappearAnimation!.RunAsync(_view, token);

            IsVisible = false;

            ShowRetryMetrics = false;
            ShowCircuitStatus = false;
            ShowRetryProgress = false;
        }
        catch (OperationCanceledException)
        {
            IsVisible = false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error hiding notification popup");
            IsVisible = false; 
        }
        finally
        {
            IsAnimating = false;
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

    private static async Task DelaySafe(TimeSpan delay, CancellationToken token)
    {
        try { await Task.Delay(delay, token); }
        catch (OperationCanceledException) { /* ignore */ }
    }

    public void Dispose()
    {
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        _uiTransitionCts?.Cancel();
        _uiTransitionCts?.Dispose();

        _disposables.Dispose();
        _statusUpdateSemaphore.Dispose();
    }
}
