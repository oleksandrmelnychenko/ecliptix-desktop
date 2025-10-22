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
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IDisposable, IResettable
{
    private const string CreateAccountKey = "CreateAccount";
    private const string SignInKey = "SignIn";

    private static readonly FrozenDictionary<string, MembershipViewType> NavigationCache =
        new Dictionary<string, MembershipViewType>
        {
            [CreateAccountKey] = MembershipViewType.MobileVerification,
            [SignInKey] = MembershipViewType.SignIn
        }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = new();

    private bool _isDisposed;

    public string UrlPathSegment => "/welcome";
    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, IRoutableViewModel> NavToSignInCommand { get; }
    [ObservableAsProperty] public bool IsCreateAccountBusy { get; }
    [ObservableAsProperty] public bool IsSignInBusy { get; }


    public WelcomeViewModel(IScreen hostScreen, ILocalizationService localizationService,
        NetworkProvider networkProvider) : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        NavToCreateAccountCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            MembershipViewType viewType = NavigationCache[CreateAccountKey];
            return hostWindow.Navigate.Execute(viewType);
        });

        NavToSignInCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            MembershipViewType viewType = NavigationCache[SignInKey];
            return hostWindow.Navigate.Execute(viewType);
        });

        NavToCreateAccountCommand.IsExecuting.ToPropertyEx(this, x => x.IsCreateAccountBusy);
        NavToSignInCommand.IsExecuting.ToPropertyEx(this, x => x.IsSignInBusy);

        _disposables.Add(NavToCreateAccountCommand);
        _disposables.Add(NavToSignInCommand);
    }

    public void ResetState()
    {

    }

    public new void Dispose()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            NavToCreateAccountCommand.Dispose();
            NavToSignInCommand.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
