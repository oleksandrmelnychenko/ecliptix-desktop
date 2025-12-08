using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Core;

public enum NetworkState
{
    Disconnected,
    Connected,
    ServerError
}

public class NetworkBadgeViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly IConnectivityService _connectivityService;
    private readonly ILocalizationService? _localizationService;

    private const string IconOnline = "M5 12.55a11 11 0 0 1 14.08 0M1.42 9a16 16 0 0 1 21.16 0M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01";
    private const string IconOffline = "M23.64 7c-.45-.34-4.93-4-11.64-4-1.5 0-2.89.19-4.15.48L18.18 13.8 23.64 7zm-6.6 8.22L3.27 1.44 2 2.72l2.05 2.06C1.91 5.17 1.5 5.48 1.5 5.48c-.5.39.06 1.07.57 1.07.21 0 .4-.09.52-.23 0 0 4.05-3.15 9.41-3.15 1.56 0 3 .26 4.31.7L18.7 6.3C17.06 5.86 15.09 5.5 13 5.5c-6.14 0-10.3 3.43-10.66 3.73l9.66 12.02c.3.37.86.37 1.16 0l2.76-3.44 3.4 3.41L20.71 20 17.04 15.22z";
    private const string IconError = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z";

    [ObservableAsProperty] public string Text { get; }
    [ObservableAsProperty] public string Icon { get; }

    [ObservableAsProperty] public string HoverText { get; }

    [ObservableAsProperty] public bool IsConnected { get; }
    [ObservableAsProperty] public bool IsDisconnected { get; }
    [ObservableAsProperty] public bool IsServerError { get; }

    public NetworkBadgeViewModel()
    {
        _connectivityService = Locator.Current.GetService<IConnectivityService>()
            ?? throw new InvalidOperationException("IConnectivityService not found in Locator");

        _localizationService = Locator.Current.GetService<ILocalizationService>();

        ConnectivitySnapshot initialSnapshot = _connectivityService.CurrentSnapshot;
        Log.Information("[BadgeVM] Init Snapshot: Source={Source}, Status={Status}", initialSnapshot.Source, initialSnapshot.Status);

        IObservable<ConnectivitySnapshot> sharedStream = _connectivityService.ConnectivityStream
            .Do(s => Log.Debug("[BadgeVM] Stream Event: {Source} -> {Status}", s.Source, s.Status)) // Лог вхідних подій
            .Publish()
            .RefCount();

        ConnectivityStatus initialInternetState = initialSnapshot.Source == ConnectivitySource.INTERNET_PROBE
            ? initialSnapshot.Status
            : (initialSnapshot.Status == ConnectivityStatus.CONNECTED ? ConnectivityStatus.CONNECTED : ConnectivityStatus.UNAVAILABLE);

        ConnectivityStatus initialServerState = initialSnapshot.Source == ConnectivitySource.DATA_CENTER
            ? initialSnapshot.Status
            : ConnectivityStatus.DISCONNECTED;

        IObservable<ConnectivityStatus> internetStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.INTERNET_PROBE)
            .Select(s => s.Status)
            .StartWith(initialInternetState)
            .Do(s => Log.Debug("[BadgeVM] Internet Status Update: {Status}", s)) // Лог зміни статусу інтернету
            .DistinctUntilChanged();

        IObservable<ConnectivityStatus> serverStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.DATA_CENTER)
            .Select(s => s.Status)
            .StartWith(initialServerState)
            .Do(s => Log.Debug("[BadgeVM] Server Status Update: {Status}", s)) // Лог зміни статусу сервера
            .DistinctUntilChanged();

        IObservable<NetworkBadgeState> badgeState = Observable.CombineLatest(
                internetStatus,
                serverStatus,
                DetermineBadgeState)
            .Do(state => Log.Information("[BadgeVM] Final Calculated State: {State}", state)) // Лог фінального рішення
            .DistinctUntilChanged();

        badgeState.Select(s => s == NetworkBadgeState.Connected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.IsConnected)
            .DisposeWith(_disposables);

        badgeState.Select(s => s == NetworkBadgeState.Disconnected)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.IsDisconnected)
            .DisposeWith(_disposables);

        badgeState.Select(s => s == NetworkBadgeState.ServerError)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.IsServerError)
            .DisposeWith(_disposables);

        if (_localizationService != null)
        {
            Observable.CombineLatest(
                    badgeState,
                    _localizationService.WhenAnyValue(x => x.CurrentCultureName),
                    (state, _) => GetTextForState(state))
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToPropertyEx(this, x => x.Text)
                .DisposeWith(_disposables);
        }
        else
        {
            badgeState.Select(GetTextForState)
                .ObserveOn(RxApp.MainThreadScheduler)
                .ToPropertyEx(this, x => x.Text)
                .DisposeWith(_disposables);
        }

        badgeState.Select(_ => string.Empty)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.HoverText)
            .DisposeWith(_disposables);

        badgeState.Select(GetIconForState)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.Icon)
            .DisposeWith(_disposables);
    }

    private NetworkBadgeState DetermineBadgeState(ConnectivityStatus internet, ConnectivityStatus server)
    {
        Log.Verbose("[BadgeVM] DetermineBadgeState Input: Internet={Internet}, Server={Server}", internet, server);

        if (internet == ConnectivityStatus.UNAVAILABLE)
        {
            Log.Verbose("[BadgeVM] Result -> Disconnected (No Internet)");
            return NetworkBadgeState.Disconnected;
        }

        if (server == ConnectivityStatus.CONNECTED)
        {
            Log.Verbose("[BadgeVM] Result -> Connected (OK)");
            return NetworkBadgeState.Connected;
        }
        Log.Verbose("[BadgeVM] Result -> ServerError");
        return NetworkBadgeState.ServerError;
    }

    private string GetTextForState(NetworkBadgeState state)
    {
        if (_localizationService == null)
        {
            return state switch
            {
                NetworkBadgeState.Connected => "Online",
                NetworkBadgeState.ServerError => "Server Error",
                _ => "Offline"
            };
        }

        return state switch
        {
            NetworkBadgeState.Connected => _localizationService[LocalizationKeys.NetworkStatus.ONLINE],
            NetworkBadgeState.ServerError => _localizationService[LocalizationKeys.NetworkStatus.SERVER_ERROR],
            _ => _localizationService[LocalizationKeys.NetworkStatus.OFFLINE]
        };
    }

    private string GetHoverTextForState(NetworkBadgeState state) => state switch
    {
        NetworkBadgeState.Connected => "You are connected to the interned",
        NetworkBadgeState.ServerError => "No connection to the server, reconnecting",
        _ => "You are not connected to the internet"
    };

    private string GetIconForState(NetworkBadgeState state) => state switch
    {
        NetworkBadgeState.Connected => IconOnline,
        NetworkBadgeState.ServerError => IconError,
        _ => IconOffline
    };

    public void Dispose()
    {
        Log.Debug("[BadgeVM] Disposing NetworkBadgeViewModel");
        _disposables.Dispose();
    }

    private enum NetworkBadgeState
    {
        Disconnected,
        Connected,
        ServerError
    }
}
