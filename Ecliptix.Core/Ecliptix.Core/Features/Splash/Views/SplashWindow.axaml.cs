using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Features.Splash.ViewModels;
using ReactiveUI;

namespace Ecliptix.Core.Features.Splash.Views;

public partial class SplashWindow : ReactiveWindow<SplashWindowViewModel>
{
    private const string ConnectedClass = "connected";
    private const string ConnectingClass = "connecting";
    private const string DisconnectedClass = "disconnected";
    private const string RestoreClass = "restore";
    private const string DefaultClass = "default";

    private static readonly FrozenDictionary<ConnectivityStatus, string> StatusClassMap =
        new Dictionary<ConnectivityStatus, string>
        {
            [ConnectivityStatus.Connected] = ConnectedClass,
            [ConnectivityStatus.Connecting] = ConnectingClass,
            [ConnectivityStatus.Recovering] = RestoreClass,
            [ConnectivityStatus.Disconnected] = DisconnectedClass,
            [ConnectivityStatus.Unavailable] = DisconnectedClass,
            [ConnectivityStatus.RetriesExhausted] = DisconnectedClass,
            [ConnectivityStatus.ShuttingDown] = DisconnectedClass
        }.ToFrozenDictionary();

    private ConnectivityStatus _currentConnectivityStatus = ConnectivityStatus.Disconnected;

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
                .Select(dc => dc!)
                .OfType<SplashWindowViewModel>()
                .SelectMany(vm => vm.WhenAnyValue(x => x.ConnectivityStatus))
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(UpdateWindowClass)
                .DisposeWith(disposables);
        });
    }

    private void UpdateWindowClass(ConnectivityStatus status)
    {
        if (_currentConnectivityStatus == status)
        {
            return;
        }

        if (_currentConnectivityStatus != ConnectivityStatus.Disconnected)
        {
            string oldClass = GetClassForStatusFast(_currentConnectivityStatus);
            Classes.Remove(oldClass);
        }

        string newClass = GetClassForStatusFast(status);
        Classes.Add(newClass);
        _currentConnectivityStatus = status;
    }

    private static string GetClassForStatusFast(ConnectivityStatus status) =>
        StatusClassMap.GetValueOrDefault(status, DefaultClass);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _currentConnectivityStatus = ConnectivityStatus.Disconnected;
        base.OnUnloaded(e);
    }
}
