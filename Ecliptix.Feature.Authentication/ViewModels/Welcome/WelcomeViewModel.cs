using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Controls.Carousels;
using Ecliptix.Core.MVVM;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;

namespace Ecliptix.Feature.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : ViewModelBase, IRoutableViewModel, IResettable
{
    private const string ROUTE_WELCOME = "/welcome";
    private const string CREATE_ACCOUNT_KEY = "CreateAccount";
    private const string SIGN_IN_KEY = "SignIn";

    private static readonly FrozenDictionary<string, MembershipViewType> NavigationCache =
        new Dictionary<string, MembershipViewType>
        {
            [CREATE_ACCOUNT_KEY] = MembershipViewType.MOBILE_VERIFICATION_VIEW,
            [SIGN_IN_KEY] = MembershipViewType.SIGN_IN_VIEW,
        }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    public WelcomeViewModel(IScreen hostScreen, ILocalizationService localizationService,
        NetworkProvider networkProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService)
    {
        HostScreen = hostScreen;
        Slides = InitializeSlides(localizationService);

        this.WhenActivated(disposables =>
        {
            CurrentSlideIndex = 0;

            this.WhenAnyValue(x => x.CurrentSlideIndex)
                .Select(_ => Observable.Timer(TimeSpan.FromSeconds(4), RxApp.TaskpoolScheduler))
                .Switch()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (Slides.Count > 0)
                    {
                        CurrentSlideIndex = (CurrentSlideIndex + 1) % Slides.Count;
                    }
                })
                .DisposeWith(disposables);
        });

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

        NavToCreateAccountCommand.IsExecuting.ToPropertyEx(this, x => x.IsCreateAccountBusy).DisposeWith(_disposables);
        NavToSignInCommand.IsExecuting.ToPropertyEx(this, x => x.IsSignInBusy).DisposeWith(_disposables);

        _disposables.Add(NavToCreateAccountCommand);
        _disposables.Add(NavToSignInCommand);
    }

    private List<WelcomeSlideItemTemplate> InitializeSlides(ILocalizationService localizationService) =>
    [
        new(
            LocalizationKeys.Welcome.Carousel.Slide1.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide1.DESCRIPTION,
            localizationService
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide2.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide2.DESCRIPTION,
            localizationService
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide3.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide3.DESCRIPTION,
            localizationService
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide4.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide4.DESCRIPTION,
            localizationService
        )
    ];

    public string UrlPathSegment => ROUTE_WELCOME;

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToCreateAccountCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToSignInCommand { get; }

    [ObservableAsProperty] public bool IsCreateAccountBusy { get; }

    [ObservableAsProperty] public bool IsSignInBusy { get; }

    public List<WelcomeSlideItemTemplate> Slides { get; }

    [Reactive]
    public int CurrentSlideIndex { get; set; }

    public void ResetState() => CurrentSlideIndex = 0;

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
