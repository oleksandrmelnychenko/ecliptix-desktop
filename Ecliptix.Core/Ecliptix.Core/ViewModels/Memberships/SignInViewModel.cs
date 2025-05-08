using System;
using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignInViewModel : ReactiveObject
{
   
    public ReactiveCommand<Unit, Unit> SignInCommand { get; }

    public SignInViewModel()
    {
        
        SignInCommand = ReactiveCommand.Create(() =>
        {
            System.Console.WriteLine($"Signing in with Mobile: , Password: ");
        });
    }
}