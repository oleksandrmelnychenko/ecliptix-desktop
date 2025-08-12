using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships.Components.Splash;

public partial class SplashWindow : ReactiveWindow<SplashWindowViewModel>
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .Cast<SplashWindowViewModel>()
                .Where(vm => vm != null)
                .SelectMany(vm => vm.WhenAnyValue(x => x.NetworkStatus))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(UpdateWindowClasses)
                .DisposeWith(disposables);
        });
    }

    private void UpdateWindowClasses(NetworkStatus status)
    {
        Classes.Remove("connected");
        Classes.Remove("connecting");
        Classes.Remove("disconnected");
        Classes.Remove("restore");
        Classes.Remove("default");

        string className = status switch
        {
            NetworkStatus.DataCenterConnected => "connected",
            NetworkStatus.DataCenterConnecting => "connecting",
            NetworkStatus.DataCenterDisconnected => "disconnected",
            NetworkStatus.RestoreSecrecyChannel => "restore",
            _ => "default"
        };

        Classes.Add(className);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
    }
}