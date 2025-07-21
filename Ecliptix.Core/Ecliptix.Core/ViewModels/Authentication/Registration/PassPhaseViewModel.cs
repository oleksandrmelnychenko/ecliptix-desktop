using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PassPhaseViewModel: ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    private readonly ILocalizationService _localizationService;
    private string _passPhase = string.Empty;

    public string PassPhase
    {
        get => _passPhase;
        set => this.RaiseAndSetIfChanged(ref _passPhase, value);
    }

    public string Title => _localizationService["Authentication.Registration.passPhase.title"];
    public string Description => _localizationService["Authentication.Registration.passPhase.description"];
    public string Hint => _localizationService["Authentication.Registration.passPhase.hint"];
    public string ButtonContent => _localizationService["Authentication.Registration.passPhase.button"];
    public string Watermark => _localizationService["Authentication.Registration.passPhase.watermark"];

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }
    public ViewModelActivator Activator { get; } = new();

    public PassPhaseViewModel(
        ILocalizationService localizationService,
        IScreen hostScreen)
    {
        _localizationService = localizationService;
        HostScreen = hostScreen;
        SubmitCommand = ReactiveCommand.Create(() =>
        {
            Console.WriteLine($"PassPhase submitted: {PassPhase}");
        });

        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(Title));
                    this.RaisePropertyChanged(nameof(Description));
                    this.RaisePropertyChanged(nameof(Hint));
                    this.RaisePropertyChanged(nameof(ButtonContent));
                    this.RaisePropertyChanged(nameof(Watermark));
                })
                .DisposeWith(disposables);
        });
    }

    public string? UrlPathSegment { get; } = "/pass-phase";
    public IScreen HostScreen { get; }
}