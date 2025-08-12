using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships.Components.Splash;

public partial class SplashWindow : ReactiveWindow<SplashWindowViewModel>
{
    private NetworkStatus _currentNetworkStatus = NetworkStatus.DataCenterDisconnected;
    
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        SetupBindings();
    }

    private void SetupBindings()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
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
            string oldClass = GetClassForStatus(_currentNetworkStatus);
            Classes.Remove(oldClass);
        }
        
        string newClass = GetClassForStatus(status);
        Classes.Add(newClass);
        _currentNetworkStatus = status;
    }

    private static string GetClassForStatus(NetworkStatus status) => status switch
    {
        NetworkStatus.DataCenterConnected => "connected",
        NetworkStatus.DataCenterConnecting => "connecting",
        NetworkStatus.DataCenterDisconnected => "disconnected",
        NetworkStatus.RestoreSecrecyChannel => "restore",
        _ => "default"
    };

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _currentNetworkStatus = NetworkStatus.DataCenterDisconnected;
        base.OnUnloaded(e);
    }
}