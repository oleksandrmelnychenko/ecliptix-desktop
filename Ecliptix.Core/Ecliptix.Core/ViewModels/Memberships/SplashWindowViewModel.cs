using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.Network.AppEvents;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase, IActivatableViewModel
{
    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; } = new();

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();
    
    private string _statusText  = "Initializing...";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }
    
    public SplashWindowViewModel()
    {
        this.WhenActivated(disposables =>
        {
            MessageBus.Current.Listen<InitializationStatusUpdate>()
                .Subscribe(update => 
                {
                    StatusText = update.Status;
                })
                .DisposeWith(disposables); 
            
            IsSubscribed.TrySetResult(true);
        });
    }
}