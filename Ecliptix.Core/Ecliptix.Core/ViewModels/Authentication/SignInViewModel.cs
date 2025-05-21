using System;
using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class SignInViewModel : ReactiveObject
{
    public SignInViewModel()
    {
        SignInCommand = ReactiveCommand.Create(() => { Console.WriteLine("Signing in with Mobile: , Password: "); });
    }

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }
}