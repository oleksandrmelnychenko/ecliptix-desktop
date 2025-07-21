using System.Reactive;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class WelcomeViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public string UrlPathSegment { get; } = "/welcome";
    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen)
    {
        HostScreen = hostScreen;

        NavToCreateAccountCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(
                MembershipViewType.PhoneVerification
            );
        });

        NavToSignInCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.SignIn);
        });
    }
}
