using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : ViewModelBase, IRoutableViewModel, IResettable
{
    private const string CREATE_ACCOUNT_KEY = "CreateAccount";
    private const string SIGN_IN_KEY = "SignIn";

    private static readonly FrozenDictionary<string, MembershipViewType> NavigationCache =
        new Dictionary<string, MembershipViewType>
        {
            [CREATE_ACCOUNT_KEY] = MembershipViewType.MOBILE_VERIFICATION_VIEW,
            [SIGN_IN_KEY] = MembershipViewType.SIGN_IN_VIEW
        }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    public WelcomeViewModel(IScreen hostScreen, ILocalizationService localizationService,
        NetworkProvider networkProvider)
        : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        NavToCreateAccountCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            ((AuthenticationViewModel)HostScreen).CurrentFlowContext = AuthenticationFlowContext.REGISTRATION;
            MembershipViewType viewType = NavigationCache[CREATE_ACCOUNT_KEY];
            return hostWindow.Navigate.Execute(viewType);
        });

        NavToSignInCommand = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            ((AuthenticationViewModel)HostScreen).CurrentFlowContext = AuthenticationFlowContext.SECURE_KEY_RECOVERY;
            MembershipViewType viewType = NavigationCache[SIGN_IN_KEY];
            return hostWindow.Navigate.Execute(viewType);
        });

        NavToCreateAccountCommand.IsExecuting.ToProperty(this, x => x.IsCreateAccountBusy).DisposeWith(_disposables);
        NavToSignInCommand.IsExecuting.ToProperty(this, x => x.IsSignInBusy).DisposeWith(_disposables);

        _disposables.Add(NavToCreateAccountCommand);
        _disposables.Add(NavToSignInCommand);
    }

    public string UrlPathSegment => "/welcome";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToCreateAccountCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToSignInCommand { get; }

    [ObservableAsProperty] public bool IsCreateAccountBusy { get; }

    [ObservableAsProperty] public bool IsSignInBusy { get; }

    public void ResetState()
    {
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            base.Dispose(disposing);
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
