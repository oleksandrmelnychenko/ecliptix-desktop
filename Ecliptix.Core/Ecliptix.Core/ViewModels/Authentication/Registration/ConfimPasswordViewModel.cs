using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ReactiveUI;
using System.Reactive.Linq;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class PasswordConfirmationViewModel : ReactiveObject
{
    private string _password = string.Empty;
    private string _verifyPassword = string.Empty;
    private string _passwordErrorMessage = string.Empty;
    private bool _isPasswordErrorVisible;
    private bool _canSubmit;

    public PasswordConfirmationViewModel()
    {
        this.WhenAnyValue(
                x => x.Password,
                x => x.VerifyPassword)
            .Subscribe(_ => ValidatePasswords());
    }

    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    public string VerifyPassword
    {
        get => _verifyPassword;
        set => this.RaiseAndSetIfChanged(ref _verifyPassword, value);
    }

    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        set => this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
    }

    public bool IsPasswordErrorVisible
    {
        get => _isPasswordErrorVisible;
        set => this.RaiseAndSetIfChanged(ref _isPasswordErrorVisible, value);
    }

    public bool CanSubmit
    {
        get => _canSubmit;
        set => this.RaiseAndSetIfChanged(ref _canSubmit, value);
    }

    private void ValidatePasswords()
    {
        if (string.IsNullOrEmpty(Password) || string.IsNullOrEmpty(VerifyPassword))
        {
            IsPasswordErrorVisible = false;
            CanSubmit = false;
            return;
        }

        if (Password != VerifyPassword)
        {
            PasswordErrorMessage = "Passwords do not match";
            IsPasswordErrorVisible = true;
            CanSubmit = false;
        }
        else
        {
            IsPasswordErrorVisible = false;
            CanSubmit = true;
        }
    }
}