using System;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using ReactiveUI;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;

namespace Ecliptix.Core.ViewModels.Authentication;

public class MembershipHostWindowModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new RoutingState();

    public ReactiveCommand<AuthViewType, IRoutableViewModel> Navigate { get; }

    public MembershipHostWindowModel(NetworkProvider networkProvider, ILocalizationService localizationService)
    {
        Navigate = ReactiveCommand.CreateFromObservable<AuthViewType, IRoutableViewModel>(viewType =>
            Router.Navigate.Execute(
                CreateViewModelForView(viewType, networkProvider, localizationService)!
            )
        );

        Navigate.Execute(AuthViewType.SignIn).Subscribe();
    }

    private IRoutableViewModel? CreateViewModelForView(
        AuthViewType viewType,
        NetworkProvider networkProvider, ILocalizationService localizationService)
    {
        return viewType switch
        {
            AuthViewType.SignIn => new SignInViewModel(networkProvider, localizationService, this),
            //AuthViewType.RegistrationWizard => dependencyResolver.GetRequiredService<RegistrationWizardViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType))
        };
    }
}