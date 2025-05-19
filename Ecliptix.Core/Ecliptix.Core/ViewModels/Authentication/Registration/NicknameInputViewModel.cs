using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class NicknameInputViewModel : ViewModelBase
{
    private string _nickname = string.Empty;

    public NicknameInputViewModel()
    {
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