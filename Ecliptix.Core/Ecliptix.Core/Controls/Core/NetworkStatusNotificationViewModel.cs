using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Constants;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Infrastructure;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Core;

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

    private string? _cachedServerNotRespondingTitle;
    private string? _cachedNoInternetTitle;
    private string? _cachedServerNotRespondingDescription;
    private string? _cachedNoInternetDescription;
    private string? _cachedRetryButtonText;

    private NetworkStatusNotification? _view;
    private Border? _mainBorder;

    private static Animation? _sharedAppearAnimation;
    private static Animation? _sharedDisappearAnimation;

    private Bitmap? _cachedNoInternetIcon;
    private Bitmap? _cachedServerNotRespondingIcon;

    private TranslateTransform? _sharedTranslateTransform;

    public TimeSpan AppearDuration { get; set; } = TimeSpan.FromMilliseconds(NetworkStatusConstants.DefaultAppearDurationMs);
    public TimeSpan DisappearDuration { get; set; } = TimeSpan.FromMilliseconds(NetworkStatusConstants.DefaultDisappearDurationMs);

    private readonly ObservableAsPropertyHelper<string> _retryButtonText;
    public string RetryButtonText => _retryButtonText.Value;

    private readonly ObservableAsPropertyHelper<bool> _isVisible;
    public bool IsVisible => _isVisible.Value;

    private readonly ObservableAsPropertyHelper<bool> _isAnimating;
    public bool IsAnimating => _isAnimating.Value;

    private readonly ObservableAsPropertyHelper<string> _statusText;
    public string StatusText => _statusText.Value;

    private readonly ObservableAsPropertyHelper<string> _statusDescription;
    public string StatusDescription => _statusDescription.Value;

    private readonly ObservableAsPropertyHelper<Bitmap?> _statusIconSource;
    public Bitmap? StatusIconSource => _statusIconSource.Value;

    private readonly ObservableAsPropertyHelper<bool> _showRetryButton;
    public bool ShowRetryButton => _showRetryButton.Value;

    private readonly ObservableAsPropertyHelper<NetworkConnectionState> _connectionState;
    public NetworkConnectionState ConnectionState => _connectionState.Value;

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }

    public void SetView(NetworkStatusNotification view)
    {
        _view = view;
        _mainBorder = view.FindControl<Border>("MainBorder");
        CreateAnimations();
    }

    public NetworkStatusNotificationViewModel(
        ILocalizationService localizationService,
        IConnectivityService connectivityService,
        IPendingRequestManager pendingRequestManager)
    {
        LocalizationService = localizationService;


        IObservable<Unit> languageTrigger = Observable.FromEvent(
                handler => LocalizationService.LanguageChanged += handler,
                handler => LocalizationService.LanguageChanged -= handler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        languageTrigger
            .Skip(1)
            .Subscribe(_ => ClearStringCache())
            .DisposeWith(_disposables);

        ConnectivitySnapshot initialSnapshot = connectivityService.CurrentSnapshot;

        IObservable<ConnectivitySnapshot> connectivitySnapshots = connectivityService.ConnectivityStream
            .Publish()
            .RefCount();

        IObservable<ManualRetryRequestedEvent> manualRetryEvents =
            Observable.Create<ManualRetryRequestedEvent>(observer =>
        {
            return connectivityService.OnManualRetryRequested(
                evt =>
                {
                    observer.OnNext(evt);
                    return Task.CompletedTask;
                },
                SubscriptionLifetime.Scoped);
        });

        IObservable<NetworkConnectionState> connectionStateObservable = connectivitySnapshots
            .Select(MapConnectionState)
            .StartWith(MapConnectionState(initialSnapshot))
            .DistinctUntilChanged();

        IObservable<string> retryButtonTextObservable = languageTrigger
            .Select(_ => GetCachedRetryButtonText());


        IObservable<string> statusTextObservable = connectionStateObservable
            .CombineLatest(languageTrigger, (state, _) => state switch
            {
                NetworkConnectionState.ServerNotResponding => GetCachedServerNotRespondingTitle(),
                _ => GetCachedNoInternetTitle(),
            });

        IObservable<string> statusDescriptionObservable = connectionStateObservable
            .CombineLatest(languageTrigger, (state, _) => state switch
            {
                NetworkConnectionState.ServerNotResponding => GetCachedServerNotRespondingDescription(),
                _ => GetCachedNoInternetDescription(),
            });

        IObservable<Bitmap?> statusIconObservable = connectionStateObservable
            .Select(LoadCachedBitmap);

        IObservable<bool> showRetryButtonObservable = Observable.Merge(
                connectivitySnapshots
                    .Where(snapshot => snapshot.Status == ConnectivityStatus.RetriesExhausted)
                    .Do(_ => LogRetryButtonShow())
                    .Select(_ => true),
                manualRetryEvents
                    .Do(_ => LogRetryButtonManualHide())
                    .Select(_ => false),
                connectivitySnapshots
                    .Where(snapshot => snapshot.Status == ConnectivityStatus.Connected)
                    .Do(snapshot => LogRetryButtonConnectionHide(snapshot))
                    .Select(_ => false)
            )
            .StartWith(false);

        IObservable<bool> isVisibleObservable = connectivitySnapshots
            .Select(snapshot => snapshot.Status switch
            {
                ConnectivityStatus.Connected => Observable.Return(true)
                    .Delay(TimeSpan.FromMilliseconds(NetworkStatusConstants.AutoHideDelayMs))
                    .Select(_ => false),
                ConnectivityStatus.RetriesExhausted or
                ConnectivityStatus.Disconnected or
                ConnectivityStatus.ShuttingDown => Observable.Return(true),
                ConnectivityStatus.Unavailable => Observable.Return(true),
                ConnectivityStatus.Connecting when snapshot.Source == ConnectivitySource.InternetProbe => Observable.Return(true),
                ConnectivityStatus.Connecting => Observable.Empty<bool>(),
                _ => Observable.Return(false)
            })
            .Switch()
            .StartWith(false);



        _connectionState = connectionStateObservable.ToProperty(this, x => x.ConnectionState).DisposeWith(_disposables);
        _statusText = statusTextObservable.ToProperty(this, x => x.StatusText).DisposeWith(_disposables);
        _statusDescription = statusDescriptionObservable.ToProperty(this, x => x.StatusDescription).DisposeWith(_disposables);
        _statusIconSource = statusIconObservable.ToProperty(this, x => x.StatusIconSource).DisposeWith(_disposables);
        _showRetryButton = showRetryButtonObservable.ToProperty(this, x => x.ShowRetryButton).DisposeWith(_disposables);
        _isVisible = isVisibleObservable.ToProperty(this, x => x.IsVisible).DisposeWith(_disposables);
        _isAnimating = Observable.Return(false).ToProperty(this, x => x.IsAnimating).DisposeWith(_disposables);
        _retryButtonText = retryButtonTextObservable.ToProperty(this, x => x.RetryButtonText).DisposeWith(_disposables);


        RetryCommand = ReactiveCommand.CreateFromTask(
            async ct =>
            {
                try
                {
                    Log.Information("Manual retry requested - attempting to retry all pending requests");

                    await connectivityService.RequestManualRetryAsync();

                    int retriedCount = await pendingRequestManager.RetryAllPendingRequestsAsync(ct);
                    Log.Information("Manual retry completed - retried {RetriedCount} pending requests", retriedCount);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during manual retry operation");
                }
            },
            showRetryButtonObservable
        ).DisposeWith(_disposables);

        connectivitySnapshots
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(snapshot =>
            {
                LogNetworkEvent(snapshot);
                HandleConnectivityVisualEffects(snapshot);
            })
            .DisposeWith(_disposables);

        isVisibleObservable
            .DistinctUntilChanged()
            .ObserveOn(RxApp.TaskpoolScheduler)
            .Subscribe(visible =>
            {
                HandleVisibilityChangeAsync(visible).ConfigureAwait(false);
            })
            .DisposeWith(_disposables);
    }

    private CancellationTokenSource? _visibilityOperationTokenSource;

    private void ClearStringCache()
    {
        _cachedServerNotRespondingTitle = null;
        _cachedNoInternetTitle = null;
        _cachedServerNotRespondingDescription = null;
        _cachedNoInternetDescription = null;
        _cachedRetryButtonText = null;
    }

    private async Task HandleVisibilityChangeAsync(bool visible)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            CancellationTokenSource? previousTokenSource = _visibilityOperationTokenSource;
            CancellationTokenSource newTokenSource = new();
            _visibilityOperationTokenSource = newTokenSource;

            try
            {
                previousTokenSource?.Cancel();
                LogVisibilityChange(visible);

                if (visible)
                    await ShowAsync(newTokenSource.Token);
                else
                    await HideAsync(newTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                LogOperationCancelled();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error changing notification visibility");
            }
            finally
            {
                if (_visibilityOperationTokenSource == newTokenSource)
                {
                    _visibilityOperationTokenSource = null;
                }
                newTokenSource.Dispose();
                previousTokenSource?.Dispose();
            }
        });
    }

    private void HandleConnectivityVisualEffects(ConnectivitySnapshot snapshot)
    {
        bool isServerIssue = snapshot.Status is ConnectivityStatus.RetriesExhausted
            or ConnectivityStatus.Disconnected
            or ConnectivityStatus.ShuttingDown;
        Dispatcher.UIThread.InvokeAsync(() => ApplyClasses(serverIssue: isServerIssue));
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

        _sharedAppearAnimation ??= new Animation
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

        _sharedDisappearAnimation ??= new Animation
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

        _sharedTranslateTransform ??= new TranslateTransform();
    }

    private async Task ShowAsync(CancellationToken token)
    {
        if (_view == null) return;

        try
        {
            _view.IsVisible = true;
            _view.RenderTransform = _sharedTranslateTransform;
            if (_sharedAppearAnimation == null) CreateAnimations();
            await _sharedAppearAnimation!.RunAsync(_view, token);
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
            if (_sharedDisappearAnimation == null) CreateAnimations();
            await _sharedDisappearAnimation!.RunAsync(_view, token);
            _view.IsVisible = false;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error hiding notification popup");
        }
    }

    private Bitmap? LoadCachedBitmap(NetworkConnectionState state)
    {
        try
        {
            return state switch
            {
                NetworkConnectionState.ServerNotResponding => _cachedServerNotRespondingIcon ??= LoadBitmapFromUri(NetworkStatusConstants.ServerNotRespondingIconUri),
                _ => _cachedNoInternetIcon ??= LoadBitmapFromUri(NetworkStatusConstants.NoInternetIconUri)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load cached bitmap for state: {State}", state);
            return null;
        }
    }

    private static Bitmap LoadBitmapFromUri(string uriString)
    {
        Uri uri = new(uriString, UriKind.Absolute);
        return new Bitmap(AssetLoader.Open(uri));
    }

    private string GetCachedServerNotRespondingTitle() => _cachedServerNotRespondingTitle ??= LocalizationService["NetworkNotification.ServerNotResponding.Title"];

    private string GetCachedNoInternetTitle() => _cachedNoInternetTitle ??= LocalizationService["NetworkNotification.NoInternet.Title"];

    private string GetCachedServerNotRespondingDescription() => _cachedServerNotRespondingDescription ??= LocalizationService["NetworkNotification.ServerNotResponding.Description"];

    private string GetCachedNoInternetDescription() => _cachedNoInternetDescription ??= LocalizationService["NetworkNotification.NoInternet.Description"];

    private string GetCachedRetryButtonText() => _cachedRetryButtonText ??= LocalizationService["NetworkNotification.Button.Retry"];

    private static NetworkConnectionState MapConnectionState(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.Unavailable => NetworkConnectionState.NoInternet,
            ConnectivityStatus.RetriesExhausted => NetworkConnectionState.ServerNotResponding,
            ConnectivityStatus.Disconnected => NetworkConnectionState.ServerNotResponding,
            ConnectivityStatus.ShuttingDown => NetworkConnectionState.ServerNotResponding,
            ConnectivityStatus.Recovering => NetworkConnectionState.ServerNotResponding,
            ConnectivityStatus.Connecting when snapshot.Source == ConnectivitySource.InternetProbe => NetworkConnectionState.NoInternet,
            ConnectivityStatus.Connecting => NetworkConnectionState.ServerNotResponding,
            _ => NetworkConnectionState.NoInternet
        };
    }

    private static void LogRetryButtonShow()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🔘 RETRY BUTTON: Showing retry button");
    }

    private static void LogRetryButtonManualHide()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🔘 RETRY BUTTON: Hiding retry button (manual retry clicked)");
    }

    private static void LogRetryButtonConnectionHide(ConnectivitySnapshot snapshot)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🔘 RETRY BUTTON: Hiding retry button (status {Status}, reason {Reason})",
                snapshot.Status, snapshot.Reason);
    }

    private static void LogNetworkEvent(ConnectivitySnapshot snapshot)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🔔 CONNECTIVITY EVENT: Status={Status}, Reason={Reason}, Source={Source}, Retry={Retry}",
                snapshot.Status,
                snapshot.Reason,
                snapshot.Source,
                snapshot.RetryAttempt);
    }

    private static void LogVisibilityChange(bool visible)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🪟 NOTIFICATION VISIBILITY: Changing visibility to {Visible}", visible);
    }

    private static void LogOperationCancelled()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
            Log.Debug("🪟 NOTIFICATION VISIBILITY: Operation was cancelled");
    }

    public void Dispose()
    {
        _visibilityOperationTokenSource?.Cancel();
        _visibilityOperationTokenSource?.Dispose();

        _disposables.Dispose();
        _statusUpdateSemaphore.Dispose();

        _cachedNoInternetIcon?.Dispose();
        _cachedServerNotRespondingIcon?.Dispose();
        _cachedNoInternetIcon = null;
        _cachedServerNotRespondingIcon = null;
    }
}
