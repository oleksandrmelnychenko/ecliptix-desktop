using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Common;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel
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