using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
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

    // SVG Paths
    private const string IconOnline = "M12.01 21.49L16.64 16.86C15.4 15.63 13.78 14.97 12.01 14.97C10.27 14.97 8.64 15.63 7.38 16.86L12.01 21.49ZM18.96 14.54L21.08 12.42C18.66 10.02 15.45 8.7 12.01 8.7C8.59 8.7 5.37 10.02 2.94 12.42L5.06 14.54C6.9 12.7 9.36 11.7 12.01 11.7C14.67 11.7 17.13 12.7 18.96 14.54ZM24 9.5L21.88 7.38C19.23 4.74 15.72 3.28 12.01 3.28C8.31 3.28 4.79 4.74 2.14 7.38L0.02 9.5C3.21 6.32 7.45 4.56 12.01 4.56C16.58 4.56 20.82 6.32 24 9.5Z";
    private const string IconOffline = "M23.64 7c-.45-.34-4.93-4-11.64-4-1.5 0-2.89.19-4.15.48L18.18 13.8 23.64 7zm-6.6 8.22L3.27 1.44 2 2.72l2.05 2.06C1.91 5.17 1.5 5.48 1.5 5.48c-.5.39.06 1.07.57 1.07.21 0 .4-.09.52-.23 0 0 4.05-3.15 9.41-3.15 1.56 0 3 .26 4.31.7L18.7 6.3C17.06 5.86 15.09 5.5 13 5.5c-6.14 0-10.3 3.43-10.66 3.73l9.66 12.02c.3.37.86.37 1.16 0l2.76-3.44 3.4 3.41L20.71 20 17.04 15.22z";
    private const string IconError = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z";

    // Output Properties
    [ObservableAsProperty] public string Text { get; }
    [ObservableAsProperty] public string Icon { get; }

    // State Flags for View Styling
    [ObservableAsProperty] public bool IsConnected { get; }
    [ObservableAsProperty] public bool IsDisconnected { get; }
    [ObservableAsProperty] public bool IsServerError { get; }

    public NetworkBadgeViewModel()
    {
        _connectivityService = Locator.Current.GetService<IConnectivityService>()
            ?? throw new InvalidOperationException("IConnectivityService not found in Locator");

        IObservable<ConnectivitySnapshot> sharedStream = _connectivityService.ConnectivityStream
            .Publish()
            .RefCount();

        ConnectivitySnapshot initialSnapshot = _connectivityService.CurrentSnapshot;

        ConnectivityStatus initialInternetState = initialSnapshot.Source == ConnectivitySource.INTERNET_PROBE
            ? initialSnapshot.Status
            : (initialSnapshot.Status == ConnectivityStatus.CONNECTED ? ConnectivityStatus.CONNECTED : ConnectivityStatus.UNAVAILABLE);

        ConnectivityStatus initialServerState = initialSnapshot.Source == ConnectivitySource.DATA_CENTER
            ? initialSnapshot.Status
            : ConnectivityStatus.DISCONNECTED;

        IObservable<ConnectivityStatus> internetStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.INTERNET_PROBE)
            .Select(s => s.Status)
            .StartWith(initialInternetState);

        IObservable<ConnectivityStatus> serverStatus = sharedStream
            .Where(s => s.Source == ConnectivitySource.DATA_CENTER)
            .Select(s => s.Status)
            .StartWith(initialServerState);

        IObservable<NetworkBadgeState> badgeState = Observable.CombineLatest(
            internetStatus,
            serverStatus,
            DetermineBadgeState)
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

        badgeState.Select(GetTextForState)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.Text)
            .DisposeWith(_disposables);

        badgeState.Select(GetIconForState)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.Icon)
            .DisposeWith(_disposables);
    }

    private NetworkBadgeState DetermineBadgeState(ConnectivityStatus internet, ConnectivityStatus server)
    {
        if (server == ConnectivityStatus.CONNECTED)
        {
            return NetworkBadgeState.Connected;
        }

        if (internet == ConnectivityStatus.UNAVAILABLE ||
            internet == ConnectivityStatus.CONNECTING)
        {
            return NetworkBadgeState.Disconnected;
        }

        switch (server)
        {
            case ConnectivityStatus.DISCONNECTED:
            case ConnectivityStatus.RETRIES_EXHAUSTED:
            case ConnectivityStatus.SHUTTING_DOWN:
            case ConnectivityStatus.UNAVAILABLE:
            case ConnectivityStatus.RECOVERING:
            case ConnectivityStatus.CONNECTING:
                return NetworkBadgeState.ServerError;

            default:
                return NetworkBadgeState.Disconnected;
        }
    }

    private string GetTextForState(NetworkBadgeState state) => state switch
    {
        NetworkBadgeState.Connected => "Online",
        NetworkBadgeState.ServerError => "Server Error",
        _ => "Offline"
    };

    private string GetIconForState(NetworkBadgeState state) => state switch
    {
        NetworkBadgeState.Connected => IconOnline,
        NetworkBadgeState.ServerError => IconError,
        _ => IconOffline
    };

    public void Dispose()
    {
        _disposables.Dispose();
    }

    private enum NetworkBadgeState
    {
        Disconnected,
        Connected,
        ServerError
    }
}
