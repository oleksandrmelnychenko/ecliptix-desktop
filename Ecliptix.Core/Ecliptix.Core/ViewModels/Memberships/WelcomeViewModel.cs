using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class WelcomeViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public string UrlPathSegment { get; } = "/welcome";
    public IScreen HostScreen { get; }

    public ILocalizationService Localization { get; }

    public ReactiveCommand<Unit, Unit> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen, ILocalizationService localizationService,
        NetworkProvider networkProvider) : base(networkProvider)
    {
        HostScreen = hostScreen;
        Localization = localizationService;

        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => Localization.LanguageChanged += handler,
                    handler => Localization.LanguageChanged -= handler
                )
                .Subscribe(_ => { this.RaisePropertyChanged(string.Empty); })
                .DisposeWith(disposables);
        });

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