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
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Core;

public sealed class ConnectivityNotificationViewModel : ReactiveObject, IDisposable
{
 private readonly ILocalizationService _localizationService;
    private readonly IConnectivityService _connectivityService;
    private readonly CompositeDisposable _disposables = new();

    private bool _requiresRestorationFeedback = false;
    private bool _disposed;

    [Reactive] public bool IsDismissed { get; private set; }

    [ObservableAsProperty] public bool IsVisible { get; }
    [ObservableAsProperty] public bool IsOffline { get; }
    [ObservableAsProperty] public bool IsServerIssue { get; }
    [ObservableAsProperty] public bool IsRestored { get; }

    [ObservableAsProperty] public string StatusText { get; }
    [ObservableAsProperty] public string StatusDescription { get; }
    [ObservableAsProperty] public bool ShowRetryButton { get; }
    [ObservableAsProperty] public string RetryButtonText { get; }

    public TimeSpan RestoredStateDuration { get; set; } = TimeSpan.FromSeconds(3);

    public ReactiveCommand<Unit, Unit> RetryCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

   public ConnectivityNotificationViewModel(
        ILocalizationService localizationService,
        IConnectivityService connectivityService,
        IPendingRequestManager pendingRequestManager)
    {
        _localizationService = localizationService;
        _connectivityService = connectivityService;

        CloseCommand = ReactiveCommand.Create(() => { IsDismissed = true; });

        IObservable<Unit> languageTrigger = CreateLanguageTrigger();
        ConnectivityObservables connectivityObservables = CreateConnectivityObservables(connectivityService);

        StatusObservables statusObservables = CreateStatusObservables(languageTrigger, connectivityObservables);

        VisibilityObservables visibilityObservables = CreateVisibilityObservables(connectivityObservables.Snapshots, connectivityObservables.ManualRetryEvents);

        statusObservables.StatusText.ToPropertyEx(this, x => x.StatusText).DisposeWith(_disposables);
        statusObservables.StatusDescription.ToPropertyEx(this, x => x.StatusDescription).DisposeWith(_disposables);
        statusObservables.RetryButtonText.ToPropertyEx(this, x => x.RetryButtonText).DisposeWith(_disposables);

        visibilityObservables.ShowRetryButton.ToPropertyEx(this, x => x.ShowRetryButton).DisposeWith(_disposables);
        visibilityObservables.IsVisible.ToPropertyEx(this, x => x.IsVisible).DisposeWith(_disposables);

        statusObservables.DetailedStatus
            .Select(s => s == DetailedConnectivityStatus.NO_INTERNET_CONNECTION ||
                         s == DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION)
            .ToPropertyEx(this, x => x.IsOffline)
            .DisposeWith(_disposables);

        statusObservables.DetailedStatus
            .DistinctUntilChanged()
            .Subscribe(_ => IsDismissed = false)
            .DisposeWith(_disposables);

        statusObservables.DetailedStatus
            .Select(s => s == DetailedConnectivityStatus.SERVER_NOT_RESPONDING ||
                         s == DetailedConnectivityStatus.RETRIES_EXHAUSTED ||
                         s == DetailedConnectivityStatus.SERVER_SHUTTING_DOWN ||
                         s == DetailedConnectivityStatus.RECONNECTING)
            .ToPropertyEx(this, x => x.IsServerIssue)
            .DisposeWith(_disposables);

        statusObservables.DetailedStatus
            .Select(s => s == DetailedConnectivityStatus.INTERNET_RESTORED ||
                         s == DetailedConnectivityStatus.SERVER_RECONNECTED)
            .ToPropertyEx(this, x => x.IsRestored)
            .DisposeWith(_disposables);

        IObservable<bool> baseVisibility = visibilityObservables.IsVisible;
        baseVisibility
            .CombineLatest(this.WhenAnyValue(x => x.IsDismissed), (isVisible, isDismissed) =>
            {
                return isVisible && !isDismissed;
            })
            .ToPropertyEx(this, x => x.IsVisible)
            .DisposeWith(_disposables);

        RetryCommand = CreateRetryCommand(connectivityService, pendingRequestManager, visibilityObservables.ShowRetryButton);
    }

    private static ConnectivityObservables CreateConnectivityObservables(IConnectivityService connectivityService)
    {
        IObservable<ConnectivitySnapshot> snapshots = connectivityService.ConnectivityStream.Publish().RefCount();

        IObservable<ManualRetryRequestedEvent> manualRetryEvents = Observable.Create<ManualRetryRequestedEvent>(observer =>
            connectivityService.OnManualRetryRequested(evt => { observer.OnNext(evt); return Task.CompletedTask; }, SubscriptionLifetime.SCOPED));

        return new ConnectivityObservables(
            snapshots,
            manualRetryEvents,
            snapshots.Where(s => s.Source == ConnectivitySource.INTERNET_PROBE),
            snapshots.Where(s => s.Source == ConnectivitySource.DATA_CENTER)
        );
    }

   private StatusObservables CreateStatusObservables(
        IObservable<Unit> languageTrigger,
        ConnectivityObservables connectivityObservables)
    {
        ConnectivitySnapshot initialInternetSnapshot = new ConnectivitySnapshot(
            _connectivityService.LastKnownInternetStatus,
            ConnectivityReason.UNKNOWN,
            ConnectivitySource.INTERNET_PROBE,
            null,
            Guid.Empty
        );

        IObservable<DetailedConnectivityStatus?> internetStatus = connectivityObservables.InternetSnapshots
            .Select(MapInternetStatus)
            .StartWith(MapInternetStatus(initialInternetSnapshot));

        ConnectivitySnapshot initialServerSnapshot = new ConnectivitySnapshot(
            _connectivityService.LastKnownServerStatus,
            ConnectivityReason.UNKNOWN,
            ConnectivitySource.DATA_CENTER,
            null,
            Guid.Empty
        );

        IObservable<DetailedConnectivityStatus?> serverStatus = connectivityObservables.ServerSnapshots
            .Select(MapServerStatus)
            .StartWith(MapServerStatus(initialServerSnapshot));

        IObservable<DetailedConnectivityStatus> detailedStatus =
            CombineInternetAndServerStatus(internetStatus, serverStatus);

        IObservable<string> statusText = detailedStatus.CombineLatest(languageTrigger, (status, _) => GetStatusText(status));
        IObservable<string> statusDescription = detailedStatus.CombineLatest(languageTrigger, (status, _) => GetStatusDescription(status));
        IObservable<string> retryButtonText = languageTrigger.Select(_ => _localizationService["NetworkNotification.Button.Retry"]);

        return new StatusObservables(detailedStatus, statusText, statusDescription, retryButtonText);
    }

    private static IObservable<DetailedConnectivityStatus> CombineInternetAndServerStatus(
        IObservable<DetailedConnectivityStatus?> internetStatus,
        IObservable<DetailedConnectivityStatus?> serverStatus)
    {
        return internetStatus.CombineLatest(serverStatus, (internet, server) =>
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
        }).DistinctUntilChanged();
    }

    private VisibilityObservables CreateVisibilityObservables(
        IObservable<ConnectivitySnapshot> snapshots,
        IObservable<ManualRetryRequestedEvent> manualRetryEvents)
    {
        IObservable<bool> showRetryButton = Observable.Merge(
            snapshots.Where(s => s.Status == ConnectivityStatus.RETRIES_EXHAUSTED).Select(_ => true),
            manualRetryEvents.Select(_ => false),
            snapshots.Where(s => s.Status == ConnectivityStatus.CONNECTED).Select(_ => false)
        ).StartWith(false);

        IObservable<bool> isVisible = snapshots
            .Select(MapSnapshotToVisibility)
            .Switch()
            .StartWith(false);

        return new VisibilityObservables(showRetryButton, isVisible);
    }

    private IObservable<bool> MapSnapshotToVisibility(ConnectivitySnapshot snapshot)
    {
        return snapshot.Status switch
        {
            ConnectivityStatus.CONNECTED => ShowTemporarilyIfRestored(),

            ConnectivityStatus.RETRIES_EXHAUSTED or
                ConnectivityStatus.DISCONNECTED or
                ConnectivityStatus.SHUTTING_DOWN or
                ConnectivityStatus.RECOVERING or
                ConnectivityStatus.UNAVAILABLE => ShowPermanently(),

            ConnectivityStatus.CONNECTING when snapshot is { Source: ConnectivitySource.INTERNET_PROBE, Reason: ConnectivityReason.INTERNET_RECOVERED }
                => ShowTemporarilyIfRestored(forceRestored: true),

            ConnectivityStatus.CONNECTING when snapshot.Source == ConnectivitySource.INTERNET_PROBE => ShowPermanently(),

            _ => Observable.Return(false)
        };
    }

    private IObservable<bool> ShowPermanently()
    {
        _requiresRestorationFeedback = true;
        return Observable.Return(true);
    }

    private IObservable<bool> ShowTemporarilyIfRestored(bool forceRestored = false)
    {
        return Observable.Defer(() =>
        {
            if (_requiresRestorationFeedback || forceRestored)
            {
                _requiresRestorationFeedback = false;
                return Observable.Timer(RestoredStateDuration, RxApp.TaskpoolScheduler)
                    .Select(_ => false)
                    .StartWith(true);
            }
            return Observable.Return(false);
        });
    }

    private ReactiveCommand<Unit, Unit> CreateRetryCommand(
        IConnectivityService service,
        IPendingRequestManager manager,
        IObservable<bool> canExecute)
    {
        return ReactiveCommand.CreateFromTask(async ct =>
        {
            try
            {
                await service.RequestManualRetryAsync();
                await manager.RetryAllPendingRequestsAsync(ct);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Manual retry failed");
            }
        }, canExecute);
    }

    private static DetailedConnectivityStatus? MapInternetStatus(ConnectivitySnapshot snapshot) => snapshot.Status switch
    {
        ConnectivityStatus.UNAVAILABLE => DetailedConnectivityStatus.NO_INTERNET_CONNECTION,
        ConnectivityStatus.CONNECTING when snapshot.Reason == ConnectivityReason.INTERNET_RECOVERED => DetailedConnectivityStatus.INTERNET_RESTORED,
        ConnectivityStatus.CONNECTING => DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION,
        _ => null
    };

    private static DetailedConnectivityStatus? MapServerStatus(ConnectivitySnapshot snapshot) => snapshot.Status switch
    {
        ConnectivityStatus.CONNECTING => DetailedConnectivityStatus.CONNECTING_TO_SERVER,
        ConnectivityStatus.RECOVERING => DetailedConnectivityStatus.RECONNECTING,
        ConnectivityStatus.RETRIES_EXHAUSTED => DetailedConnectivityStatus.RETRIES_EXHAUSTED,
        ConnectivityStatus.SHUTTING_DOWN => DetailedConnectivityStatus.SERVER_SHUTTING_DOWN,
        ConnectivityStatus.DISCONNECTED => DetailedConnectivityStatus.SERVER_NOT_RESPONDING,
        ConnectivityStatus.CONNECTED => DetailedConnectivityStatus.SERVER_RECONNECTED,
        _ => null
    };

    private IObservable<Unit> CreateLanguageTrigger() => Observable.FromEvent(
            h => _localizationService.LanguageChanged += h,
            h => _localizationService.LanguageChanged -= h)
        .Select(_ => Unit.Default)
        .StartWith(Unit.Default);

    private string GetStatusText(DetailedConnectivityStatus status)
    {
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
        return _localizationService[key];
    }

    private string GetStatusDescription(DetailedConnectivityStatus status)
    {
        string key = status switch
        {
            DetailedConnectivityStatus.NO_INTERNET_CONNECTION => "NetworkNotification.NoInternet.Description",
            DetailedConnectivityStatus.CHECKING_INTERNET_CONNECTION => "NetworkNotification.CheckingInternet.Description",
            DetailedConnectivityStatus.INTERNET_RESTORED => "NetworkNotification.InternetRestored.Description",
            DetailedConnectivityStatus.CONNECTING_TO_SERVER => "NetworkNotification.Connecting.Description",
            DetailedConnectivityStatus.RECONNECTING => "NetworkNotification.Reconnecting.Description",
            DetailedConnectivityStatus.SERVER_NOT_RESPONDING => "NetworkNotification.ServerNotResponding.Description",
            DetailedConnectivityStatus.SERVER_SHUTTING_DOWN => "NetworkNotification.ServerShuttingDown.Description",
            DetailedConnectivityStatus.RETRIES_EXHAUSTED => "NetworkNotification.RetriesExhausted.Description",
            DetailedConnectivityStatus.SERVER_RECONNECTED => "NetworkNotification.ServerReconnected.Description",
            _ => "NetworkNotification.NoInternet.Description"
        };
        return _localizationService[key];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposables.Dispose();
        _disposed = true;
    }

    private readonly record struct ConnectivityObservables(
        IObservable<ConnectivitySnapshot> Snapshots,
        IObservable<ManualRetryRequestedEvent> ManualRetryEvents,
        IObservable<ConnectivitySnapshot> InternetSnapshots,
        IObservable<ConnectivitySnapshot> ServerSnapshots);

    private readonly record struct StatusObservables(
        IObservable<DetailedConnectivityStatus> DetailedStatus,
        IObservable<string> StatusText,
        IObservable<string> StatusDescription,
        IObservable<string> RetryButtonText);

    private readonly record struct VisibilityObservables(IObservable<bool> ShowRetryButton, IObservable<bool> IsVisible);
}
