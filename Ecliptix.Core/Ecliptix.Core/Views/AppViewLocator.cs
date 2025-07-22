using System;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Core.Views.Memberships.SignIn;
using Ecliptix.Core.Views.Memberships.SignUp;
using ReactiveUI; 

namespace Ecliptix.Core.Views;

public class AppViewLocator : IViewLocator
{
    public IViewFor ResolveView<T>(T viewModel, string contract = null) => viewModel switch
    {
        SignInViewModel context => new SignInView { DataContext = context },
        WelcomeViewModel context => new WelcomeView { DataContext = context },
        PhoneVerificationViewModel context => new PhoneVerificationView { DataContext = context },
        VerificationCodeEntryViewModel context => new VerificationCodeEntryView { DataContext = context },
        PasswordConfirmationViewModel context => new PasswordConfirmationView { DataContext = context },
        
        _ => throw new ArgumentOutOfRangeException(nameof(viewModel))
    };
}
