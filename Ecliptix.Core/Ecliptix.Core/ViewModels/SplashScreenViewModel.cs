using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Network.AppEvents;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.ViewModels;

public class SplashScreenViewModel : ReactiveObject, IActivatableViewModel
{
    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; }

    [Reactive]
    public string StatusText { get; set; } = "Initializing...";
    
    public SplashScreenViewModel()
    {
        Activator = new ViewModelActivator();

        this.WhenActivated(disposables =>
        {
            MessageBus.Current.Listen<ConnectionFailedUiEvent>()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(eventData =>
                {
                    //TODO: Handle connection failure
                    
                    
                })
                .DisposeWith(disposables);

            Observable.Timer(TimeSpan.FromSeconds(2))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => { })
                .DisposeWith(disposables);
        });
    }
}