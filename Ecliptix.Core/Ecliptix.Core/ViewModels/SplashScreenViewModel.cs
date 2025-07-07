using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Network.AppEvents;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Core.ViewModels;

public class SplashScreenViewModel : ReactiveObject, IActivatableViewModel
{
    private string _status = "Starting...";

    public string Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; }

    public SplashScreenViewModel()
    {
        Activator = new ViewModelActivator();

        this.WhenActivated(disposables =>
        {
            Status = "Initializing application...";

            MessageBus.Current.Listen<ConnectionFailedUiEvent>()
                .ObserveOn(RxApp.MainThreadScheduler) 
                .Subscribe(eventData =>
                {
                    Status = $"Connection failed: {eventData.Reason}";
                    Log.Error("Connection failed: {Message}", eventData.Reason);
                })
                .DisposeWith(disposables);

            // Simulate initialization or loading process (replace with actual logic)
            Observable.Timer(TimeSpan.FromSeconds(2)) // Simulate async work
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ => Status = "Initialization complete")
                .DisposeWith(disposables);
        });
    }
}