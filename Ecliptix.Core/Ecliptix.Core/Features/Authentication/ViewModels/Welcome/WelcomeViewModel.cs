using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Common;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IDisposable, IResettable
{
    private static readonly FrozenDictionary<string, MembershipViewType> NavigationCache =
        new Dictionary<string, MembershipViewType>()
        {
            ["CreateAccount"] = MembershipViewType.MobileVerification,
            ["SignIn"] = MembershipViewType.SignIn
        }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    public string UrlPathSegment => "/welcome";
    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, IRoutableViewModel> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen, ISystemEventService systemEventService, ILocalizationService localizationService,
        NetworkProvider networkProvider) : base(systemEventService, networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        NavToCreateAccountCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            MembershipHostWindowModel hostWindow = (MembershipHostWindowModel)HostScreen;
            MembershipViewType viewType = NavigationCache["CreateAccount"];
            return hostWindow.Navigate.Execute(viewType);
        });

        NavToSignInCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            MembershipHostWindowModel hostWindow = (MembershipHostWindowModel)HostScreen;
            MembershipViewType viewType = NavigationCache["SignIn"];
            return hostWindow.Navigate.Execute(viewType);
        });

        _disposables.Add(NavToCreateAccountCommand);
        _disposables.Add(NavToSignInCommand);
    }

    public void ResetState()
    {
        if (_isDisposed) return;
        // Welcome screen has no resettable state
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            NavToCreateAccountCommand?.Dispose();
            NavToSignInCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}