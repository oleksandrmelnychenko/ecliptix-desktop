using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Core.Controls.Carousels;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : ViewModelBase, IRoutableViewModel, IResettable
{
    private const string ROUTE_WELCOME = "/welcome";
    private const string CREATE_ACCOUNT_KEY = "CreateAccount";
    private const string SIGN_IN_KEY = "SignIn";

    private const string MENTAL_PROTECTION_IMAGE_PATH = "avares://Ecliptix.Core/Assets/DataSeed/mentalprotection.png";
    private const string SMART_COMMUNITIES_IMAGE_PATH = "avares://Ecliptix.Core/Assets/DataSeed/smartcommunities.png";
    private const string WELLNESS_IMAGE_PATH = "avares://Ecliptix.Core/Assets/DataSeed/wellness.png";

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
            null,
            localizationService,
            SlideType.Custom
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide2.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide2.DESCRIPTION,
            LoadImage(MENTAL_PROTECTION_IMAGE_PATH),
            localizationService
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide3.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide3.DESCRIPTION,
            LoadImage(SMART_COMMUNITIES_IMAGE_PATH),
            localizationService
        ),
        new(
            LocalizationKeys.Welcome.Carousel.Slide4.TITLE,
            LocalizationKeys.Welcome.Carousel.Slide4.DESCRIPTION,
            LoadImage(WELLNESS_IMAGE_PATH),
            localizationService
        )
    ];

    private Bitmap? LoadImage(string path)
    {
        try
        {
            Uri uri = new(path);
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception)
        {
            return null;
        }
    }

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
