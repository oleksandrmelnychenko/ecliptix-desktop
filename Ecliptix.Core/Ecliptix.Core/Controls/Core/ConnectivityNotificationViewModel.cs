using System;
using System.Collections.Generic;
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

public enum ConnectivityIssueCategory
{
    InternetUnavailable,
    ServerUnreachable
}

public enum DetailedConnectivityStatus
{
    NoInternetConnection,
    CheckingInternetConnection,
    InternetRestored,
    ConnectingToServer,
    Reconnecting,
    ServerNotResponding,
    ServerShuttingDown,
    RetriesExhausted,
    ServerReconnected
}

public sealed class ConnectivityNotificationViewModel : ReactiveObject, IDisposable
{
    public ILocalizationService LocalizationService { get; }

    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statusUpdateSemaphore = new(1, 1);

    private readonly Dictionary<DetailedConnectivityStatus, string> _cachedStatusTexts = new();
    private readonly Dictionary<DetailedConnectivityStatus, string> _cachedStatusDescriptions = new();
    private string? _cachedRetryButtonText;

    private ConnectivityNotificationView? _view;
    private Border? _mainBorder;
    private bool _disposed;

    private static Animation? _sharedAppearAnimation;
    private static Animation? _sharedDisappearAnimation;

    private Bitmap? _cachedInternetUnavailableIcon;
    private Bitmap? _cachedServerUnreachableIcon;

    private TranslateTransform? _sharedTranslateTransform;

    public TimeSpan AppearDuration { get; set; } =
        TimeSpan.FromMilliseconds(NetworkStatusConstants.DEFAULT_APPEAR_DURATION_MS);

    public TimeSpan DisappearDuration { get; set; } =
        TimeSpan.FromMilliseconds(NetworkStatusConstants.DEFAULT_DISAPPEAR_DURATION_MS);

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

    private readonly ObservableAsPropertyHelper<ConnectivityIssueCategory> _issueCategory;
    public ConnectivityIssueCategory IssueCategory => _issueCategory.Value;

    private readonly ObservableAsPropertyHelper<DetailedConnectivityStatus> _detailedStatus;
    public DetailedConnectivityStatus DetailedStatus => _detailedStatus.Value;

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }

    public void SetView(ConnectivityNotificationView view)
    {
        _view = view;
        _mainBorder = view.FindControl<Border>("MainBorder");
        CreateAnimations();
    }

    public ConnectivityNotificationViewModel(
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

        IObservable<ConnectivitySnapshot> internetSnapshots = connectivitySnapshots
            .Where(snapshot => snapshot.Source == ConnectivitySource.InternetProbe);

        IObservable<ConnectivitySnapshot> serverSnapshots = connectivitySnapshots
            .Where(snapshot => snapshot.Source == ConnectivitySource.DataCenter);

        IObservable<DetailedConnectivityStatus?> internetStatusObservable = internetSnapshots
            .Select(MapInternetStatus)
            .StartWith(initialSnapshot.Source == ConnectivitySource.InternetProbe
                ? MapInternetStatus(initialSnapshot)
                : null);

        IObservable<DetailedConnectivityStatus?> serverStatusObservable = serverSnapshots
            .Select(MapServerStatus)
            .StartWith(initialSnapshot.Source == ConnectivitySource.DataCenter
                ? MapServerStatus(initialSnapshot)
                : null);

        IObservable<DetailedConnectivityStatus> detailedStatusObservable = internetStatusObservable
            .CombineLatest(serverStatusObservable, (internet, server) =>
            {
                if (internet == DetailedConnectivityStatus.NoInternetConnection ||
                    internet == DetailedConnectivityStatus.CheckingInternetConnection)
                {
                    return internet.Value;
                }

                if (server.HasValue)
                {
                    return server.Value;
                }

                return DetailedConnectivityStatus.NoInternetConnection;
            })
            .DistinctUntilChanged();

        IObservable<ConnectivityIssueCategory> issueCategoryObservable = detailedStatusObservable
            .Select(status => status switch
            {
                DetailedConnectivityStatus.NoInternetConnection or
                DetailedConnectivityStatus.CheckingInternetConnection or
                DetailedConnectivityStatus.InternetRestored => ConnectivityIssueCategory.InternetUnavailable,
                _ => ConnectivityIssueCategory.ServerUnreachable
            })
            .DistinctUntilChanged();

        IObservable<string> retryButtonTextObservable = languageTrigger
            .Select(_ => GetCachedRetryButtonText());

        IObservable<string> statusTextObservable = detailedStatusObservable
            .CombineLatest(languageTrigger, (status, _) => GetStatusText(status));

        IObservable<string> statusDescriptionObservable = detailedStatusObservable
            .CombineLatest(languageTrigger, (status, _) => GetStatusDescription(status));

        IObservable<Bitmap?> statusIconObservable = issueCategoryObservable
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
                    .Do(LogRetryButtonConnectionHide)
                    .Select(_ => false)
            )
            .StartWith(false);

        IObservable<bool> isVisibleObservable = connectivitySnapshots
            .Select(snapshot => snapshot.Status switch
            {
                ConnectivityStatus.Connected when snapshot.Source == ConnectivitySource.InternetProbe => Observable
                    .Return(false),
                ConnectivityStatus.Connected => Observable.Return(true)
                    .Delay(TimeSpan.FromMilliseconds(NetworkStatusConstants.AUTO_HIDE_DELAY_MS))
                    .Select(_ => false),
                ConnectivityStatus.RetriesExhausted or
                    ConnectivityStatus.Disconnected or
                    ConnectivityStatus.ShuttingDown or
                    ConnectivityStatus.Recovering => Observable.Return(true),
                ConnectivityStatus.Unavailable => Observable.Return(true),
                ConnectivityStatus.Connecting when snapshot.Source == ConnectivitySource.InternetProbe
                                                   && snapshot.Reason == ConnectivityReason.InternetRecovered =>
                    Observable.Return(false),
                ConnectivityStatus.Connecting when snapshot.Source == ConnectivitySource.InternetProbe => Observable
                    .Return(true),
                ConnectivityStatus.Connecting => Observable.Empty<bool>(),
                _ => Observable.Return(false)
            })
            .Switch()
            .StartWith(false);

        _issueCategory = issueCategoryObservable.ToProperty(this, x => x.IssueCategory).DisposeWith(_disposables);
        _detailedStatus = detailedStatusObservable.ToProperty(this, x => x.DetailedStatus).DisposeWith(_disposables);
        _statusText = statusTextObservable.ToProperty(this, x => x.StatusText).DisposeWith(_disposables);
        _statusDescription = statusDescriptionObservable.ToProperty(this, x => x.StatusDescription)
            .DisposeWith(_disposables);
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
                    Log.Error(ex, "ERROR during manual retry operation");
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
            .Subscribe(visible => { HandleVisibilityChangeAsync(visible).ConfigureAwait(false); })
            .DisposeWith(_disposables);
    }

    private CancellationTokenSource? _visibilityOperationTokenSource;

    private void ClearStringCache()
    {
        _cachedStatusTexts.Clear();
        _cachedStatusDescriptions.Clear();
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
                {
                    await ShowAsync(newTokenSource.Token);
                }
                else
                {
                    await HideAsync(newTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                LogOperationCancelled();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ERROR changing notification visibility");
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
        if (_mainBorder == null)
        {
            return;
        }

        if (serverIssue)
        {
            _mainBorder.Classes.Add("CircuitOpen");
        }
        else
        {
            _mainBorder.Classes.Remove("CircuitOpen");
        }

        _mainBorder.Classes.Remove("Retrying");
    }

    private void CreateAnimations()
    {
        if (_view == null)
        {
            return;
        }

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
        if (_view == null)
        {
            return;
        }

        try
        {
            _view.IsVisible = true;
            _view.RenderTransform = _sharedTranslateTransform;
            if (_sharedAppearAnimation == null)
            {
                CreateAnimations();
            }
            await _sharedAppearAnimation!.RunAsync(_view, token);
        }
        catch (OperationCanceledException)
        {
            // Animation cancelled - expected during rapid state changes
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ERROR showing notification popup");
        }
    }

    private async Task HideAsync(CancellationToken token)
    {
        if (_view == null)
        {
            return;
        }

        try
        {
            if (_sharedDisappearAnimation == null)
            {
                CreateAnimations();
            }
            await _sharedDisappearAnimation!.RunAsync(_view, token);
            _view.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Animation cancelled - expected during rapid state changes
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ERROR hiding notification popup");
        }
    }

    private Bitmap? LoadCachedBitmap(ConnectivityIssueCategory category)
    {
        try
        {
            return category switch
            {
                ConnectivityIssueCategory.ServerUnreachable => _cachedServerUnreachableIcon ??=
                    LoadBitmapFromUri(NetworkStatusConstants.SERVER_NOT_RESPONDING_ICON_URI),
                _ => _cachedInternetUnavailableIcon ??=
                    LoadBitmapFromUri(NetworkStatusConstants.NO_INTERNET_ICON_URI)
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load cached bitmap for category: {Category}", category);
            return null;
        }
    }

    private static Bitmap LoadBitmapFromUri(string uriString)
    {
        Uri uri = new(uriString, UriKind.Absolute);
        return new Bitmap(AssetLoader.Open(uri));
    }

    private string GetStatusText(DetailedConnectivityStatus status)
    {
        if (_cachedStatusTexts.TryGetValue(status, out string? cached))
        {
            return cached;
        }

        string key = status switch
        {
            DetailedConnectivityStatus.NoInternetConnection => "NetworkNotification.NoInternet.Title",
            DetailedConnectivityStatus.CheckingInternetConnection => "NetworkNotification.CheckingInternet.Title",
            DetailedConnectivityStatus.InternetRestored => "NetworkNotification.InternetRestored.Title",
            DetailedConnectivityStatus.ConnectingToServer => "NetworkNotification.Connecting.Title",
            DetailedConnectivityStatus.Reconnecting => "NetworkNotification.Reconnecting.Title",
            DetailedConnectivityStatus.ServerNotResponding => "NetworkNotification.ServerNotResponding.Title",
            DetailedConnectivityStatus.ServerShuttingDown => "NetworkNotification.ServerShuttingDown.Title",
            DetailedConnectivityStatus.RetriesExhausted => "NetworkNotification.RetriesExhausted.Title",
            DetailedConnectivityStatus.ServerReconnected => "NetworkNotification.ServerReconnected.Title",
            _ => "NetworkNotification.NoInternet.Title"
        };

        string text = LocalizationService[key];
        _cachedStatusTexts[status] = text;
        return text;
    }

    private string GetStatusDescription(DetailedConnectivityStatus status)
    {
        if (_cachedStatusDescriptions.TryGetValue(status, out string? cached))
        {
            return cached;
        }

        string key = status switch
        {
            DetailedConnectivityStatus.NoInternetConnection => "NetworkNotification.NoInternet.Description",
            DetailedConnectivityStatus.CheckingInternetConnection => "NetworkNotification.CheckingInternet.Description",
            DetailedConnectivityStatus.InternetRestored => "NetworkNotification.InternetRestored.Description",
            DetailedConnectivityStatus.ConnectingToServer => "NetworkNotification.Connecting.Description",
            DetailedConnectivityStatus.Reconnecting => "NetworkNotification.Reconnecting.Description",
            DetailedConnectivityStatus.ServerNotResponding => "NetworkNotification.ServerNotResponding.Description",
            DetailedConnectivityStatus.ServerShuttingDown => "NetworkNotification.ServerShuttingDown.Description",
            DetailedConnectivityStatus.RetriesExhausted => "NetworkNotification.RetriesExhausted.Description",
            DetailedConnectivityStatus.ServerReconnected => "NetworkNotification.ServerReconnected.Description",
            _ => "NetworkNotification.NoInternet.Description"
        };

        string text = LocalizationService[key];
        _cachedStatusDescriptions[status] = text;
        return text;
    }

    private string GetCachedRetryButtonText() =>
        _cachedRetryButtonText ??= LocalizationService["NetworkNotification.Button.Retry"];

    private static DetailedConnectivityStatus? MapInternetStatus(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.Unavailable => DetailedConnectivityStatus.NoInternetConnection,

            ConnectivityStatus.Connecting when snapshot.Reason == ConnectivityReason.InternetRecovered
                => DetailedConnectivityStatus.InternetRestored,

            ConnectivityStatus.Connecting => DetailedConnectivityStatus.CheckingInternetConnection,

            _ => null
        };
    }

    private static DetailedConnectivityStatus? MapServerStatus(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.Connecting => DetailedConnectivityStatus.ConnectingToServer,

            ConnectivityStatus.Recovering => DetailedConnectivityStatus.Reconnecting,

            ConnectivityStatus.RetriesExhausted => DetailedConnectivityStatus.RetriesExhausted,

            ConnectivityStatus.ShuttingDown => DetailedConnectivityStatus.ServerShuttingDown,

            ConnectivityStatus.Disconnected => DetailedConnectivityStatus.ServerNotResponding,

            ConnectivityStatus.Connected => DetailedConnectivityStatus.ServerReconnected,

            _ => null
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _visibilityOperationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Token source already disposed - safe to ignore during cleanup
        }
        finally
        {
            _visibilityOperationTokenSource?.Dispose();
            _visibilityOperationTokenSource = null;
        }

        _disposables.Dispose();
        _statusUpdateSemaphore.Dispose();

        _cachedInternetUnavailableIcon?.Dispose();
        _cachedServerUnreachableIcon?.Dispose();
        _cachedInternetUnavailableIcon = null;
        _cachedServerUnreachableIcon = null;

        _cachedStatusTexts.Clear();
        _cachedStatusDescriptions.Clear();

        _disposed = true;
    }

    private static void LogRetryButtonShow()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🔘 RETRY BUTTON: Showing retry button");
        }
    }

    private static void LogRetryButtonManualHide()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🔘 RETRY BUTTON: Hiding retry button (manual retry clicked)");
        }
    }

    private static void LogRetryButtonConnectionHide(ConnectivitySnapshot snapshot)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🔘 RETRY BUTTON: Hiding retry button (status {Status}, reason {Reason})",
                snapshot.Status, snapshot.Reason);

        }
    }

    private static void LogNetworkEvent(ConnectivitySnapshot snapshot)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🔔 CONNECTIVITY EVENT: Status={Status}, Reason={Reason}, Source={Source}, Retry={Retry}",
                snapshot.Status,
                snapshot.Reason,
                snapshot.Source,
                snapshot.RetryAttempt);

        }
    }

    private static void LogVisibilityChange(bool visible)
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🪟 NOTIFICATION VISIBILITY: Changing visibility to {Visible}", visible);
        }
    }

    private static void LogOperationCancelled()
    {
        if (Log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
        {
            Log.Debug("🪟 NOTIFICATION VISIBILITY: Operation was cancelled");
        }
    }
}
