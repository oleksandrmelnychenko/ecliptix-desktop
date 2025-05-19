using System.Reactive;
using Ecliptix.Core.Services;

using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class NicknameInputViewModel : ViewModelBase
{
    private string _nickname = string.Empty;
    
    private readonly ILocalizationService _localizationService;
    public string Title => _localizationService["Authentication.Registration.nicknameInput.title"];
    public string Description => _localizationService["Authentication.Registration.nicknameInput.description"];
    public string HintText => _localizationService["Authentication.Registration.nicknameInput.hint"];
    public string ButtonContent => _localizationService["Authentication.Registration.nicknameInput.button"];
    public string Watermark => _localizationService["Authentication.Registration.nicknameInput.watermark"];
    
    public NicknameInputViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        
        _localizationService.LanguageChanged += () =>
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(Description));
            this.RaisePropertyChanged(nameof(HintText));
            this.RaisePropertyChanged(nameof(ButtonContent));
            this.RaisePropertyChanged(nameof(Watermark));
        };
        
   
        SubmitCommand = ReactiveCommand.Create(() =>
        {
            // Add your command logic here
            System.Console.WriteLine($"Nickname submitted: {Nickname}");
        });
    }

    public string Nickname
    {
        get => _nickname;
        set => this.RaiseAndSetIfChanged(ref _nickname, value);
    }

    public ReactiveCommand<Unit, Unit> SubmitCommand { get; }

}