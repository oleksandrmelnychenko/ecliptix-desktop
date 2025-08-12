using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships.Components.Splash;

public partial class SplashWindow : ReactiveWindow<SplashWindowViewModel>
{
    private const string ConnectedClass = "connected";
    private const string ConnectingClass = "connecting";
    private const string DisconnectedClass = "disconnected";
    private const string RestoreClass = "restore";
    private const string DefaultClass = "default";

    private static readonly FrozenDictionary<NetworkStatus, string> StatusClassMap =
        new Dictionary<NetworkStatus, string>
        {
            [NetworkStatus.DataCenterConnected] = ConnectedClass,
            [NetworkStatus.DataCenterConnecting] = ConnectingClass,
            [NetworkStatus.DataCenterDisconnected] = DisconnectedClass,
            [NetworkStatus.RestoreSecrecyChannel] = RestoreClass
        }.ToFrozenDictionary();

    private NetworkStatus _currentNetworkStatus = NetworkStatus.DataCenterDisconnected;

    public SplashWindow()
    {
        InitializeComponent();
        SetupPrecompiledBindings();
    }

    private void SetupPrecompiledBindings()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .Where(dc => dc != null)
                .OfType<SplashWindowViewModel>()
                .SelectMany(vm => vm.WhenAnyValue(x => x.NetworkStatus))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(UpdateWindowClass)
                .DisposeWith(disposables);
        });
    }

    private void UpdateWindowClass(NetworkStatus status)
    {
        if (_currentNetworkStatus == status) return;

        if (_currentNetworkStatus != NetworkStatus.DataCenterDisconnected)
        {
            string oldClass = GetClassForStatusFast(_currentNetworkStatus);
            Classes.Remove(oldClass);
        }

        string newClass = GetClassForStatusFast(status);
        Classes.Add(newClass);
        _currentNetworkStatus = status;
    }

    private static string GetClassForStatusFast(NetworkStatus status) =>
        StatusClassMap.GetValueOrDefault(status, DefaultClass);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _currentNetworkStatus = NetworkStatus.DataCenterDisconnected;
        base.OnUnloaded(e);
    }
}