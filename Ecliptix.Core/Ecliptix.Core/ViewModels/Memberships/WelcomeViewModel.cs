using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class WelcomeViewModel : ViewModelBase, IRoutableViewModel
{
    private static readonly FrozenDictionary<string, MembershipViewType> NavigationCache = 
        new Dictionary<string, MembershipViewType>()
        {
            ["CreateAccount"] = MembershipViewType.MobileVerification,
            ["SignIn"] = MembershipViewType.SignIn
        }.ToFrozenDictionary();
    
    public string UrlPathSegment => "/welcome";
    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen, ISystemEvents systemEvents, ILocalizationService localizationService,
        NetworkProvider networkProvider) : base(systemEvents, networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        NavToCreateAccountCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(
                NavigationCache["CreateAccount"]
            );
        });

        NavToSignInCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(NavigationCache["SignIn"]);
        });
    }
}