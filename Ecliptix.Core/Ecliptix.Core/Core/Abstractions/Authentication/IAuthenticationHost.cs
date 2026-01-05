using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.Modularity.Abstractions.Authentication;

public interface IAuthenticationHost : IScreen
{
    AuthenticationFlowContext CurrentFlowContext { get; set; }

    ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; }

    void ClearNavigationStack(bool preserveInitialWelcome = false, MembershipViewType? preserveViewType = null);
}
