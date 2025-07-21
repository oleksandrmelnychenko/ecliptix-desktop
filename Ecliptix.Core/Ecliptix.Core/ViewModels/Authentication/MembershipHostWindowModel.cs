using System;
using Avalonia.Media;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class MembershipHostWindowModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new RoutingState();

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    public MembershipHostWindowModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService
    )
    {
        Navigate = ReactiveCommand.CreateFromObservable<MembershipViewType, IRoutableViewModel>(
            viewType => Router.Navigate.Execute(
                CreateViewModelForView(viewType, networkProvider, localizationService)!
            ));

        Navigate.Execute(MembershipViewType.MembershipWelcome).Subscribe();

        this.WhenAnyObservable(x => x.Router.NavigateBack.CanExecute)
            .Subscribe(canExecute =>
            {
                CanNavigateBack = canExecute;
            });
    }

    private bool _canNavigateBack;
    public bool CanNavigateBack
    {
        get => _canNavigateBack;
        private set => this.RaiseAndSetIfChanged(ref _canNavigateBack, value);
    }

    private IRoutableViewModel CreateViewModelForView(
        MembershipViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService
    )
    {
        return viewType switch
        {
            MembershipViewType.SignIn => new SignInViewModel(networkProvider, localizationService, this),
            MembershipViewType.MembershipWelcome => new WelcomeViewModel(this),
            MembershipViewType.PhoneVerification => new PhoneVerificationViewModel(networkProvider, localizationService, this),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType)),
        };
    }
}
