using System;
using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignInViewModel : ReactiveObject
{
    private string _mobile = string.Empty;
    private string _password = string.Empty;
    private string _mobileError = string.Empty;
    private double _mobileEllipseOpacity = 0.0;
    private string _passwordError = string.Empty;
    private double _passwordEllipseOpacity = 0.0;
    
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

    public string MobileError
    {
        get => _mobileError;
        set => this.RaiseAndSetIfChanged(ref _mobileError, value);
    }

    public double MobileEllipseOpacity
    {
        get => _mobileEllipseOpacity;
        set => this.RaiseAndSetIfChanged(ref _mobileEllipseOpacity, value);
    }

    public string PasswordError
    {
        get => _passwordError;
        set => this.RaiseAndSetIfChanged(ref _passwordError, value);
    }

    public double PasswordEllipseOpacity
    {
        get => _passwordEllipseOpacity;
        set => this.RaiseAndSetIfChanged(ref _passwordEllipseOpacity, value);
    }
    
    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    public SignInViewModel()
    {
        // Watch Password changes
        this.WhenAnyValue(x => x.Password)
            .Subscribe((password) =>
            {
                if (string.IsNullOrEmpty(password))
                {
                    PasswordError = "Password cannot be empty";
                    PasswordEllipseOpacity = 1.0;
                }
                else
                {
                    PasswordError = "Password cannot be empty";
                    PasswordEllipseOpacity = 0.0;
                }
            });
        
        // Watch Mobile changes
        this.WhenAnyValue(x => x.Mobile)
            .Subscribe(mobile =>
            {
                if (string.IsNullOrEmpty(mobile))
                {
                    MobileError = "Mobile number cannot be empty";
                    MobileEllipseOpacity = 1.0;
                }
                else
                {
                    MobileError = "Mobile number cannot be empty";
                    MobileEllipseOpacity = 0.0;
                }
            });

        
        SignInCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine($"Signing in with Mobile: {Mobile}, Password: {Password}");
        });
    }
}