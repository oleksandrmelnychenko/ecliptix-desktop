using System;
using System.Linq;
using System.Reactive;
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
    private NetworkStatusNotification? _view;
    private Ellipse? _statusEllipse;
    private Border? _mainBorder;
    private CancellationTokenSource? _retryCts;
    private IDisposable? _queueSubscription;
    private IDisposable? _operationSubscription;
    
    // Animation objects
    private Animation? _flickerAnimation;
    private Animation? _appearAnimation;
    private Animation? _disappearAnimation;
    
    // Animation durations
    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(250);
    public TimeSpan FlickerDuration { get; set; } = TimeSpan.FromMilliseconds(1500);
    
    private bool _isVisible = false;
    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }
    
    private bool _isAnimating = false;
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
    
    private bool _showRetryMetrics = false;
    public bool ShowRetryMetrics
    {
        get => _showRetryMetrics;
        set => this.RaiseAndSetIfChanged(ref _showRetryMetrics, value);
    }
    
    private int _currentAttempt = 0;
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
    
    private bool _showCircuitStatus = false;
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
    
    private bool _isRetrying = false;
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
    
    private bool _showRetryProgress = false;
    public bool ShowRetryProgress
    {
        get => _showRetryProgress;
        set => this.RaiseAndSetIfChanged(ref _showRetryProgress, value);
    }
    
    private double _retryProgress = 0;
    public double RetryProgress
    {
        get => _retryProgress;
        set => this.RaiseAndSetIfChanged(ref _retryProgress, value);
    }
    
    private IBrush _retryProgressColor = new SolidColorBrush(Color.Parse("#84cd57"));
    public IBrush RetryProgressColor
    {
        get => _retryProgressColor;
        set => this.RaiseAndSetIfChanged(ref _retryProgressColor, value);
    }
    
    private bool _showNextRetryCountdown = false;
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

        // Subscribe to network status changes
        _networkEvents.NetworkStatusChanged
            .Subscribe(evt =>
            {
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await HandleNetworkStatusChange(evt);
                });
            });
        
        // Queue subscriptions removed - retry handled by Polly
        // _queueSubscription = null;
        // _operationSubscription = null;
        
        // Manual retry command
        RequestManualRetryCommand = ReactiveCommand.CreateFromTask(
            RetryAllOperationsAsync,
            this.WhenAnyValue(x => x.CanRetry, x => x.IsRetrying,
                (canRetry, isRetrying) => canRetry && !isRetrying));
    }
    
    private async Task HandleNetworkStatusChange(NetworkStatusChangedEvent evt)
    {
        switch (evt.State)
        {
            case NetworkStatus.RetriesExhausted:
                StatusText = "Connection Failed";
                StatusDescription = $"{_pendingRequestManager.PendingRequestCount} requests pending";
                ShowRetryMetrics = true;
                UpdateRetryMetrics();
                CanRetry = true;
                
                if (!IsVisible)
                {
                    await ShowAsync();
                }
                
                // Apply circuit open style
                if (_mainBorder != null)
                {
                    _mainBorder.Classes.Add("CircuitOpen");
                    _mainBorder.Classes.Remove("Retrying");
                }
                break;
                
            case NetworkStatus.DataCenterDisconnected:
            case NetworkStatus.ServerShutdown:
                StatusText = "Server Unavailable";
                StatusDescription = "Click retry to reconnect";
                ShowRetryMetrics = true;
                CanRetry = true;
                
                if (!IsVisible)
                {
                    await ShowAsync();
                }
                break;
                
            case NetworkStatus.DataCenterConnected:
                StatusText = "Connected";
                StatusDescription = "Connection restored";
                
                // Automatically retry pending requests on reconnection
                await RetryPendingRequestsAsync();
                
                if (IsVisible)
                {
                    // Show success briefly before hiding
                    await Task.Delay(2000);
                    await HideAsync();
                }
                break;
        }
    }
    
    private void UpdateQueueStatus(dynamic evt)
    {
        switch (evt.NewState)
        {
            case 1: // Retrying
                IsRetrying = true;
                CanRetry = false;
                RetryButtonTooltip = "Retrying operations...";
                ShowRetryProgress = true;
                StatusDescription = $"Retrying {evt.FailedOperations} failed operations...";
                
                // Apply retrying style
                if (_mainBorder != null)
                {
                    _mainBorder.Classes.Add("Retrying");
                    _mainBorder.Classes.Remove("CircuitOpen");
                }
                break;
                
            case 0: // Idle
                IsRetrying = false;
                CanRetry = evt.FailedOperations > 0;
                ShowRetryProgress = false;
                RetryButtonTooltip = evt.FailedOperations > 0
                    ? $"Retry {evt.FailedOperations} failed operations"
                    : "No operations to retry";
                
                if (evt.FailedOperations == 0)
                {
                    StatusText = "All Operations Successful";
                    StatusDescription = "All queued operations completed";
                }
                else
                {
                    StatusDescription = $"{evt.FailedOperations} operations still failed";
                }
                
                // Remove style classes
                if (_mainBorder != null)
                {
                    _mainBorder.Classes.Remove("Retrying");
                    _mainBorder.Classes.Remove("CircuitOpen");
                }
                break;
        }
    }
    
    private void UpdateOperationStatus(dynamic evt)
    {
        // Update current attempt counter
        if (evt.NewState == 1) // InProgress
        {
            CurrentAttempt++;
        }
        
        // Update metrics
        UpdateRetryMetrics();
    }
    
    private void UpdateRetryMetrics()
    {
        var metrics = _retryStrategy.GetRetryMetrics();
        
        if (metrics.TotalAttempts > 0)
        {
            var rate = (double)metrics.SuccessfulAttempts / metrics.TotalAttempts * 100;
            SuccessRate = $"{rate:F0}%";
            
            // Color code success rate
            SuccessRateColor = rate switch
            {
                >= 80 => new SolidColorBrush(Color.Parse("#84cd57")),
                >= 50 => new SolidColorBrush(Color.Parse("#ffa500")),
                _ => new SolidColorBrush(Color.Parse("#d81c1c"))
            };
        }
        
        // Circuit breaker status can be shown based on metrics if needed
        // For now, keeping it simple
    }
    
    private async Task<int> RetryPendingRequestsAsync()
    {
        try
        {
            // Retry all pending requests
            var successCount = await _pendingRequestManager.RetryAllPendingRequestsAsync(_retryCts?.Token ?? CancellationToken.None);
            Log.Information("Retried {Count} pending requests", successCount);
            return successCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error retrying pending requests");
            return 0;
        }
    }
    
    private async Task RetryAllOperationsAsync()
    {
        try
        {
            Log.Information("User initiated manual retry with SecrecyChannel restoration");
            
            // Cancel any existing retry operation
            _retryCts?.Cancel();
            _retryCts = new CancellationTokenSource();
            
            // Update UI state
            IsRetrying = true;
            CanRetry = false;
            CurrentAttempt = 0;
            StatusText = "Restoring Connection";
            StatusDescription = "Establishing secure channel...";
            
            // Step 1: Reset the retry strategy (clears circuit breakers and metrics)
            _retryStrategy.ResetConnectionState();
            
            // Step 2: Force re-establish SecrecyChannel for all connections
            // Assuming default connection ID is 1 (you may need to adjust this)
            uint connectId = 1;
            
            var establishResult = await _networkProvider.EstablishSecrecyChannelAsync(connectId);
            
            if (establishResult.IsErr)
            {
                StatusText = "Connection Failed";
                StatusDescription = $"Failed to establish secure channel: {establishResult.UnwrapErr().Message}";
                Log.Error("Failed to establish SecrecyChannel: {Error}", establishResult.UnwrapErr().Message);
                return;
            }
            
            StatusText = "Connection Restored";
            StatusDescription = "Retrying pending requests...";
            
            // Step 3: Retry all pending requests
            var successCount = await RetryPendingRequestsAsync();
            var totalPending = _pendingRequestManager.PendingRequestCount;
            
            if (successCount == totalPending && totalPending > 0)
            {
                StatusText = "All Requests Successful";
                StatusDescription = $"{successCount} requests completed successfully";
                
                // Hide notification after success
                await Task.Delay(3000);
                await HideAsync();
            }
            else if (successCount > 0)
            {
                var remainingCount = _pendingRequestManager.PendingRequestCount;
                StatusText = "Partial Success";
                StatusDescription = $"{successCount} succeeded, {remainingCount} still pending";
            }
            else
            {
                StatusText = "Connection Restored";
                StatusDescription = "Ready to process new requests";
                
                // Hide after showing success
                await Task.Delay(2000);
                await HideAsync();
            }
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
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -20d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
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
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 1d),
                        new Setter(TranslateTransform.YProperty, 0d)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0d),
                        new Setter(TranslateTransform.YProperty, -15d)
                    }
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
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(Visual.OpacityProperty, 0.3d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters = { new Setter(Visual.OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(Visual.OpacityProperty, 0.3d) }
                }
            }
        };
    }

    private async Task ShowAsync()
    {
        if (_view == null || IsAnimating) return;
        
        IsAnimating = true;
        
        _view.RenderTransform = new TranslateTransform();
        IsVisible = true;
        
        if (_appearAnimation == null)
        {
            CreateAnimations();
        }

        await _appearAnimation!.RunAsync(_view);
        
        StartFlickerAnimation();
        IsAnimating = false;
    }

    private async Task HideAsync()
    {
        if (_view == null || IsAnimating) return;
        
        IsAnimating = true;
        
        StopFlickerAnimation();
        
        if (_disappearAnimation == null)
        {
            CreateAnimations();
        }

        await _disappearAnimation!.RunAsync(_view);
        
        IsVisible = false;
        IsAnimating = false;
        
        // Reset states
        ShowRetryMetrics = false;
        ShowCircuitStatus = false;
        ShowRetryProgress = false;
    }

    private void StartFlickerAnimation()
    {
        if (_flickerAnimation == null || _statusEllipse == null)
        {
            CreateAnimations();
        }

        _flickerAnimation?.RunAsync(_statusEllipse!);
    }

    private void StopFlickerAnimation()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (_statusEllipse != null)
            {
                _statusEllipse.Opacity = 1.0;
            }
        }
        else
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_statusEllipse != null)
                {
                    _statusEllipse.Opacity = 1.0;
                }
            });
        }
    }
    
    public void Dispose()
    {
        _retryCts?.Cancel();
        _retryCts?.Dispose();
        _queueSubscription?.Dispose();
        _operationSubscription?.Dispose();
    }
}