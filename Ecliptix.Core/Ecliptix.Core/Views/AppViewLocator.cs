using System;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views.Memberships.SignIn;
using ReactiveUI;

namespace Ecliptix.Core.Views;

public class AppViewLocator : IViewLocator
{
    public IViewFor ResolveView<T>(T viewModel, string contract = null) => viewModel switch
    {
        SignInViewModel context => new SignInView { DataContext = context },
        _ => throw new ArgumentOutOfRangeException(nameof(viewModel))
    };
}