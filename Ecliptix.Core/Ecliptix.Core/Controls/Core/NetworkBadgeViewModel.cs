using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.Controls.Core;

public sealed class NetworkBadgeViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly ILocalizationService? _localizationService;

    [ObservableAsProperty] public string Text { get; } = default!;
    [ObservableAsProperty] public string Icon { get; } = default!;

    [ObservableAsProperty] public string HoverText { get; } = default!;

    [ObservableAsProperty] public bool IsConnected { get; }
    [ObservableAsProperty] public bool IsDisconnected { get; }
    [ObservableAsProperty] public bool IsServerError { get; }

    public NetworkBadgeViewModel(ILocalizationService localizationService, IConnectivityService connectivityService)
    {
        _localizationService = localizationService;

        ConnectivityStatus initialInternetState = connectivityService.LastKnownInternetStatus;
        ConnectivityStatus initialServerState = connectivityService.LastKnownServerStatus;

        Log.Information("[BadgeVM] Init State: Internet={Internet}, Server={Server}", initialInternetState, initialServerState);

        IObservable<ConnectivitySnapshot> sharedStream = connectivityService.ConnectivityStream
            .Do(s => Log.Debug("[BadgeVM] Stream Event: {Source} -> {Status}", s.Source, s.Status))
            .Publish()
            .RefCount();

        IObservable<ConnectivityStatus> internetStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.INTERNET_PROBE)
            .Select(s => s.Status)
            .StartWith(initialInternetState)
            .Do(s => Log.Debug("[BadgeVM] Internet Status Update: {Status}", s))
            .DistinctUntilChanged();

        IObservable<ConnectivityStatus> serverStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.DATA_CENTER)
            .Select(s => s.Status)
            .StartWith(initialServerState)
            .Do(s => Log.Debug("[BadgeVM] Server Status Update: {Status}", s))
            .DistinctUntilChanged();

        IObservable<NetworkBadgeState> badgeState = Observable.CombineLatest(
                connectivityService.InternetStatus, 
                connectivityService.ServerStatus,   
                DetermineBadgeState)
            .Do(state => Log.Information("[BadgeVM] Final Calculated State: {State}", state))
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

        IObservable<System.Reactive.Unit> languageChangedTrigger;

        if (_localizationService != null)
        {
            languageChangedTrigger = Observable.FromEvent(
                    h => _localizationService.LanguageChanged += h,
                    h => _localizationService.LanguageChanged -= h)
                .Select(_ => System.Reactive.Unit.Default)
                .StartWith(System.Reactive.Unit.Default);
        }
        else
        {
            languageChangedTrigger = Observable.Return(System.Reactive.Unit.Default);
        }

        Observable.CombineLatest(
                badgeState,
                languageChangedTrigger,
                (state, _) => GetTextForState(state))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.Text)
            .DisposeWith(_disposables);

    }

    private NetworkBadgeState DetermineBadgeState(ConnectivityStatus internet, ConnectivityStatus server)
    {
        Log.Verbose("[BadgeVM] DetermineBadgeState Input: Internet={Internet}, Server={Server}", internet, server);

        if (internet == ConnectivityStatus.UNAVAILABLE)
        {
            return NetworkBadgeState.Disconnected;
        }

        if (server == ConnectivityStatus.CONNECTED)
        {
            return NetworkBadgeState.Connected;
        }

        if (server == ConnectivityStatus.CONNECTING)
        {
            return NetworkBadgeState.Disconnected;
        }

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
