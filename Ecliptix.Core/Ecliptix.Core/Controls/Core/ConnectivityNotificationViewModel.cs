using System;
using System.Collections.Generic;
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
using Ecliptix.Core.Controls.Common;
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

public sealed class ConnectivityNotificationViewModel : ReactiveObject, IDisposable
{
    private readonly ILocalizationService _localizationService;

    private readonly CompositeDisposable _disposables = new();
    private readonly SemaphoreSlim _statusUpdateSemaphore = new(1, 1);

    private readonly Dictionary<DetailedConnectivityStatus, string> _cachedStatusTexts = new();
    private readonly Dictionary<DetailedConnectivityStatus, string> _cachedStatusDescriptions = new();
    private string? _cachedRetryButtonText;

    private ConnectivityNotificationView? _view;
    private Border? _mainBorder;
    private bool _disposed;

    private static readonly Lazy<Animation> SharedAppearAnimation = new(CreateAppearAnimation);
    private static readonly Lazy<Animation> SharedDisappearAnimation = new(CreateDisappearAnimation);

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

    private readonly ObservableAsPropertyHelper<ConnectivityErrorType> _issueCategory;
    public ConnectivityErrorType ErrorType => _issueCategory.Value;

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
        _localizationService = localizationService;

        IObservable<Unit> languageTrigger = CreateLanguageTrigger();
        ConnectivityObservables connectivityObservables = CreateConnectivityObservables(connectivityService);
        StatusObservables statusObservables =
            CreateStatusObservables(languageTrigger, connectivityObservables, connectivityService.CurrentSnapshot);
        VisibilityObservables visibilityObservables =
            CreateVisibilityObservables(connectivityObservables.Snapshots, connectivityObservables.ManualRetryEvents);

        _issueCategory = statusObservables.IssueCategory.ToProperty(this, x => x.ErrorType).DisposeWith(_disposables);
        _detailedStatus = statusObservables.DetailedStatus.ToProperty(this, x => x.DetailedStatus)
            .DisposeWith(_disposables);
        _statusText = statusObservables.StatusText.ToProperty(this, x => x.StatusText).DisposeWith(_disposables);
        _statusDescription = statusObservables.StatusDescription.ToProperty(this, x => x.StatusDescription)
            .DisposeWith(_disposables);
        _statusIconSource = statusObservables.StatusIcon.ToProperty(this, x => x.StatusIconSource)
            .DisposeWith(_disposables);
        _showRetryButton = visibilityObservables.ShowRetryButton.ToProperty(this, x => x.ShowRetryButton)
            .DisposeWith(_disposables);
        _isVisible = visibilityObservables.IsVisible.ToProperty(this, x => x.IsVisible).DisposeWith(_disposables);
        _isAnimating = Observable.Return(false).ToProperty(this, x => x.IsAnimating).DisposeWith(_disposables);
        _retryButtonText = statusObservables.RetryButtonText.ToProperty(this, x => x.RetryButtonText)
            .DisposeWith(_disposables);

        RetryCommand = CreateRetryCommand(
            connectivityService, pendingRequestManager, visibilityObservables.ShowRetryButton);

        SetupConnectivitySubscriptions(connectivityObservables.Snapshots, visibilityObservables.IsVisible);
    }

    private void SetupConnectivitySubscriptions(
        IObservable<ConnectivitySnapshot> connectivitySnapshots,
        IObservable<bool> isVisibleObservable)
    {
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

    private IObservable<Unit> CreateLanguageTrigger()
    {
        IObservable<Unit> languageTrigger = Observable.FromEvent(
                handler => _localizationService.LanguageChanged += handler,
                handler => _localizationService.LanguageChanged -= handler)
            .Select(_ => Unit.Default)
            .StartWith(Unit.Default);

        languageTrigger
            .Skip(1)
            .Subscribe(_ => ClearStringCache())
            .DisposeWith(_disposables);

        return languageTrigger;
    }

    private static ConnectivityObservables CreateConnectivityObservables(IConnectivityService connectivityService)
    {
        IObservable<ConnectivitySnapshot> snapshots = connectivityService.ConnectivityStream
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
                    SubscriptionLifetime.SCOPED);
            });

        IObservable<ConnectivitySnapshot> internetSnapshots = snapshots
            .Where(snapshot => snapshot.Source == ConnectivitySource.INTERNET_PROBE);

        IObservable<ConnectivitySnapshot> serverSnapshots = snapshots
            .Where(snapshot => snapshot.Source == ConnectivitySource.DATA_CENTER);

        return new ConnectivityObservables(snapshots, manualRetryEvents, internetSnapshots, serverSnapshots);
    }

    private StatusObservables CreateStatusObservables(
        IObservable<Unit> languageTrigger,
        ConnectivityObservables connectivityObservables,
        ConnectivitySnapshot initialSnapshot)
    {
        IObservable<DetailedConnectivityStatus?> internetStatus = connectivityObservables.InternetSnapshots
            .Select(MapInternetStatus)
            .StartWith(initialSnapshot.Source == ConnectivitySource.INTERNET_PROBE
                ? MapInternetStatus(initialSnapshot)
                : null);

        IObservable<DetailedConnectivityStatus?> serverStatus = connectivityObservables.ServerSnapshots
            .Select(MapServerStatus)
            .StartWith(initialSnapshot.Source == ConnectivitySource.DATA_CENTER
                ? MapServerStatus(initialSnapshot)
                : null);

        IObservable<DetailedConnectivityStatus> detailedStatus =
            CombineInternetAndServerStatus(internetStatus, serverStatus);

        IObservable<ConnectivityErrorType> issueCategory = detailedStatus
            .Select(MapToIssueCategory)
            .DistinctUntilChanged();

        IObservable<string> retryButtonText = languageTrigger
            .Select(_ => GetCachedRetryButtonText());

        IObservable<string> statusText = detailedStatus
            .CombineLatest(languageTrigger, (status, _) => GetStatusText(status));

        IObservable<string> statusDescription = detailedStatus
            .CombineLatest(languageTrigger, (status, _) => GetStatusDescription(status));

        IObservable<Bitmap?> statusIcon = issueCategory.Select(LoadCachedBitmap);

        return new StatusObservables(
            issueCategory, detailedStatus, statusText, statusDescription, statusIcon, retryButtonText);
    }

    private static IObservable<DetailedConnectivityStatus> CombineInternetAndServerStatus(
        IObservable<DetailedConnectivityStatus?> internetStatus,
        IObservable<DetailedConnectivityStatus?> serverStatus)
    {
        return internetStatus
            .CombineLatest(serverStatus, (internet, server) =>
            {
                if (internet == DetailedConnectivityStatus.NO_INTERNET_CONNECTION ||
                    internet == DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION)
                {
                    return internet.Value;
                }

                if (server.HasValue)
                {
                    return server.Value;
                }

                return DetailedConnectivityStatus.NO_INTERNET_CONNECTION;
            })
            .DistinctUntilChanged();
    }

    private static ConnectivityErrorType MapToIssueCategory(DetailedConnectivityStatus status) =>
        status switch
        {
            DetailedConnectivityStatus.NO_INTERNET_CONNECTION or
                DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION or
                DetailedConnectivityStatus.INTERNET_RESTORED => ConnectivityErrorType.INTERNET_UNAVAILABLE,
            _ => ConnectivityErrorType.SERVER_UNREACHABLE
        };

    private static VisibilityObservables CreateVisibilityObservables(
        IObservable<ConnectivitySnapshot> snapshots,
        IObservable<ManualRetryRequestedEvent> manualRetryEvents)
    {
        IObservable<bool> showRetryButton = Observable.Merge(
                snapshots
                    .Where(snapshot => snapshot.Status == ConnectivityStatus.RETRIES_EXHAUSTED)
                    .Do(_ => LogRetryButtonShow())
                    .Select(_ => true),
                manualRetryEvents
                    .Do(_ => LogRetryButtonManualHide())
                    .Select(_ => false),
                snapshots
                    .Where(snapshot => snapshot.Status == ConnectivityStatus.CONNECTED)
                    .Do(LogRetryButtonConnectionHide)
                    .Select(_ => false)
            )
            .StartWith(false);

        IObservable<bool> isVisible = snapshots
            .Select(MapSnapshotToVisibility)
            .Switch()
            .StartWith(false);

        return new VisibilityObservables(showRetryButton, isVisible);
    }

    private static IObservable<bool> MapSnapshotToVisibility(ConnectivitySnapshot snapshot) =>
        snapshot.Status switch
        {
            ConnectivityStatus.CONNECTED when snapshot.Source == ConnectivitySource.INTERNET_PROBE =>
                Observable.Return(false),
            ConnectivityStatus.CONNECTED => Observable.Return(true)
                .Delay(TimeSpan.FromMilliseconds(NetworkStatusConstants.AUTO_HIDE_DELAY_MS))
                .Select(_ => false),
            ConnectivityStatus.RETRIES_EXHAUSTED or
                ConnectivityStatus.DISCONNECTED or
                ConnectivityStatus.SHUTTING_DOWN or
                ConnectivityStatus.RECOVERING or
                ConnectivityStatus.UNAVAILABLE => Observable.Return(true),
            ConnectivityStatus.CONNECTING when snapshot is
                    { Source: ConnectivitySource.INTERNET_PROBE, Reason: ConnectivityReason.INTERNET_RECOVERED } =>
                Observable.Return(false),
            ConnectivityStatus.CONNECTING when snapshot.Source == ConnectivitySource.INTERNET_PROBE =>
                Observable.Return(true),
            ConnectivityStatus.CONNECTING => Observable.Empty<bool>(),
            _ => Observable.Return(false)
        };

    private ReactiveCommand<Unit, Unit> CreateRetryCommand(
        IConnectivityService connectivityService,
        IPendingRequestManager pendingRequestManager,
        IObservable<bool> canExecuteObservable)
    {
        ReactiveCommand<Unit, Unit> command = ReactiveCommand.CreateFromTask(
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
            canExecuteObservable);

        command.DisposeWith(_disposables);
        return command;
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
        bool isServerIssue = snapshot.Status is ConnectivityStatus.RETRIES_EXHAUSTED
            or ConnectivityStatus.DISCONNECTED
            or ConnectivityStatus.SHUTTING_DOWN;
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

    private static Animation CreateAppearAnimation() => new()
    {
        Duration = TimeSpan.FromMilliseconds(NetworkStatusConstants.DEFAULT_APPEAR_DURATION_MS),
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
                    new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d)
                }
            }
        }
    };

    private static Animation CreateDisappearAnimation() => new()
    {
        Duration = TimeSpan.FromMilliseconds(NetworkStatusConstants.DEFAULT_DISAPPEAR_DURATION_MS),
        Easing = new QuadraticEaseIn(),
        FillMode = FillMode.Both,
        Children =
        {
            new KeyFrame
            {
                Cue = new Cue(0d),
                Setters =
                {
                    new Setter(Visual.OpacityProperty, 1d), new Setter(TranslateTransform.YProperty, 0d)
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

    private void CreateAnimations()
    {
        if (_view == null)
        {
            return;
        }

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
            CreateAnimations();
            await SharedAppearAnimation.Value.RunAsync(_view, token);
        }
        catch (OperationCanceledException)
        {
            // Animation canceled - expected during rapid state changes
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
            CreateAnimations();
            await SharedDisappearAnimation.Value.RunAsync(_view, token);
            _view.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Animation canceled - expected during rapid state changes
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ERROR hiding notification popup");
        }
    }

    private Bitmap? LoadCachedBitmap(ConnectivityErrorType category)
    {
        try
        {
            return category switch
            {
                ConnectivityErrorType.SERVER_UNREACHABLE => _cachedServerUnreachableIcon ??=
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
            DetailedConnectivityStatus.NO_INTERNET_CONNECTION => "NetworkNotification.NoInternet.Title",
            DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION => "NetworkNotification.CheckingInternet.Title",
            DetailedConnectivityStatus.INTERNET_RESTORED => "NetworkNotification.InternetRestored.Title",
            DetailedConnectivityStatus.CONNECTING_TO_SERVER => "NetworkNotification.Connecting.Title",
            DetailedConnectivityStatus.RECONNECTING => "NetworkNotification.Reconnecting.Title",
            DetailedConnectivityStatus.SERVER_NOT_RESPONDING => "NetworkNotification.ServerNotResponding.Title",
            DetailedConnectivityStatus.SERVER_SHUTTING_DOWN => "NetworkNotification.ServerShuttingDown.Title",
            DetailedConnectivityStatus.RETRIES_EXHAUSTED => "NetworkNotification.RetriesExhausted.Title",
            DetailedConnectivityStatus.SERVER_RECONNECTED => "NetworkNotification.ServerReconnected.Title",
            _ => "NetworkNotification.NoInternet.Title"
        };

        string text = _localizationService[key];
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
            DetailedConnectivityStatus.NO_INTERNET_CONNECTION => "NetworkNotification.NoInternet.Description",
            DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION =>
                "NetworkNotification.CheckingInternet.Description",
            DetailedConnectivityStatus.INTERNET_RESTORED => "NetworkNotification.InternetRestored.Description",
            DetailedConnectivityStatus.CONNECTING_TO_SERVER => "NetworkNotification.Connecting.Description",
            DetailedConnectivityStatus.RECONNECTING => "NetworkNotification.Reconnecting.Description",
            DetailedConnectivityStatus.SERVER_NOT_RESPONDING => "NetworkNotification.ServerNotResponding.Description",
            DetailedConnectivityStatus.SERVER_SHUTTING_DOWN => "NetworkNotification.ServerShuttingDown.Description",
            DetailedConnectivityStatus.RETRIES_EXHAUSTED => "NetworkNotification.RetriesExhausted.Description",
            DetailedConnectivityStatus.SERVER_RECONNECTED => "NetworkNotification.ServerReconnected.Description",
            _ => "NetworkNotification.NoInternet.Description"
        };

        string text = _localizationService[key];
        _cachedStatusDescriptions[status] = text;
        return text;
    }

    private string GetCachedRetryButtonText() =>
        _cachedRetryButtonText ??= _localizationService["NetworkNotification.Button.Retry"];

    private static DetailedConnectivityStatus? MapInternetStatus(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.UNAVAILABLE => DetailedConnectivityStatus.NO_INTERNET_CONNECTION,

            ConnectivityStatus.CONNECTING when snapshot.Reason == ConnectivityReason.INTERNET_RECOVERED
                => DetailedConnectivityStatus.INTERNET_RESTORED,

            ConnectivityStatus.CONNECTING => DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION,

            _ => null
        };
    }

    private static DetailedConnectivityStatus? MapServerStatus(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.CONNECTING => DetailedConnectivityStatus.CONNECTING_TO_SERVER,

            ConnectivityStatus.RECOVERING => DetailedConnectivityStatus.RECONNECTING,

            ConnectivityStatus.RETRIES_EXHAUSTED => DetailedConnectivityStatus.RETRIES_EXHAUSTED,

            ConnectivityStatus.SHUTTING_DOWN => DetailedConnectivityStatus.SERVER_SHUTTING_DOWN,

            ConnectivityStatus.DISCONNECTED => DetailedConnectivityStatus.SERVER_NOT_RESPONDING,

            ConnectivityStatus.CONNECTED => DetailedConnectivityStatus.SERVER_RECONNECTED,

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

    private readonly record struct ConnectivityObservables(
        IObservable<ConnectivitySnapshot> Snapshots,
        IObservable<ManualRetryRequestedEvent> ManualRetryEvents,
        IObservable<ConnectivitySnapshot> InternetSnapshots,
        IObservable<ConnectivitySnapshot> ServerSnapshots);

    private readonly record struct StatusObservables(
        IObservable<ConnectivityErrorType> IssueCategory,
        IObservable<DetailedConnectivityStatus> DetailedStatus,
        IObservable<string> StatusText,
        IObservable<string> StatusDescription,
        IObservable<Bitmap?> StatusIcon,
        IObservable<string> RetryButtonText);

    private readonly record struct VisibilityObservables(
        IObservable<bool> ShowRetryButton,
        IObservable<bool> IsVisible);
}
