using System;
using System.Reactive;
using System.Reactive.Concurrency; // For IScheduler
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Services; 
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class NicknameInputViewModel : ViewModelBase, IActivatableViewModel
{
    private string _nickname = string.Empty;
    public string Nickname
    {
        get => _nickname;
        set => this.RaiseAndSetIfChanged(ref _nickname, value);
    }

    private readonly ILocalizationService _localizationService;

    public string Title => _localizationService["Authentication.Registration.nicknameInput.title"];
    public string Description => _localizationService["Authentication.Registration.nicknameInput.description"];
    public string HintText => _localizationService["Authentication.Registration.nicknameInput.hint"];
    public string ButtonContent => _localizationService["Authentication.Registration.nicknameInput.button"];
    public string Watermark => _localizationService["Authentication.Registration.nicknameInput.watermark"];

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }
    public ViewModelActivator Activator { get; } = new();

    public NicknameInputViewModel(
        ILocalizationService localizationService,
        IScheduler? mainThreadScheduler = null) 
    {
        _localizationService = localizationService;
            
        IScheduler scheduler = mainThreadScheduler ?? RxApp.MainThreadScheduler;

        SubmitCommand = ReactiveCommand.Create(() =>
        {
            Console.WriteLine($"Nickname submitted: {Nickname}");
        });
        
        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handlerAction => _localizationService.LanguageChanged += handlerAction,
                    handlerAction => _localizationService.LanguageChanged -= handlerAction
                )
                .ObserveOn(scheduler) 
                .Subscribe(_ => 
                {
                    this.RaisePropertyChanged(nameof(Title));
                    this.RaisePropertyChanged(nameof(Description));
                    this.RaisePropertyChanged(nameof(HintText));
                    this.RaisePropertyChanged(nameof(ButtonContent));
                    this.RaisePropertyChanged(nameof(Watermark));
                })
                .DisposeWith(disposables); 

            SubmitCommand.ThrownExceptions
                .ObserveOn(scheduler) 
                .Subscribe(ex =>
                {
                    Console.WriteLine($"Error during SubmitCommand: {ex.Message}");
                })
                .DisposeWith(disposables);
        });
    }
}