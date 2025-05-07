using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignInViewModel : ReactiveObject
{
    private string _mobile = string.Empty;
    private string _password = string.Empty;

    public string Mobile
    {
        get => _mobile;
        set => this.RaiseAndSetIfChanged(ref _mobile, value);
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    public SignInViewModel()
    {
        SignInCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine($"Signing in with Mobile: {Mobile}, Password: {Password}");
        });
    }
}