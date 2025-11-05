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
    private const string CONNECTED_CLASS = "connected";
    private const string CONNECTING_CLASS = "connecting";
    private const string DISCONNECTED_CLASS = "disconnected";
    private const string RESTORE_CLASS = "restore";
    private const string DEFAULT_CLASS = "default";

    private static readonly FrozenDictionary<ConnectivityStatus, string> StatusClassMap =
        new Dictionary<ConnectivityStatus, string>
        {
            [ConnectivityStatus.Connected] = CONNECTED_CLASS,
            [ConnectivityStatus.Connecting] = CONNECTING_CLASS,
            [ConnectivityStatus.Recovering] = RESTORE_CLASS,
            [ConnectivityStatus.Disconnected] = DISCONNECTED_CLASS,
            [ConnectivityStatus.Unavailable] = DISCONNECTED_CLASS,
            [ConnectivityStatus.RetriesExhausted] = DISCONNECTED_CLASS,
            [ConnectivityStatus.ShuttingDown] = DISCONNECTED_CLASS
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
        StatusClassMap.GetValueOrDefault(status, DEFAULT_CLASS);

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _currentConnectivityStatus = ConnectivityStatus.Disconnected;
        base.OnUnloaded(e);
    }
}
