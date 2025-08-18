using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PassPhaseViewModel : ViewModelBase, IRoutableViewModel
{
    private string _passPhase = string.Empty;

    public string PassPhase
    {
        get => _passPhase;
        set => this.RaiseAndSetIfChanged(ref _passPhase, value);
    }

    public string? UrlPathSegment { get; } = "/pass-phase";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }

    public PassPhaseViewModel(
        ISystemEvents systemEvents,
        ILocalizationService localizationService,
        IScreen hostScreen, NetworkProvider networkProvider) : base(systemEvents, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        SubmitCommand = ReactiveCommand.Create(() => { Console.WriteLine($"PassPhase submitted: {PassPhase}"); });
    }
}