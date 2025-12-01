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
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

public sealed class WelcomeViewModel : ViewModelBase, IRoutableViewModel, IResettable, IActivatableViewModel
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


    public ViewModelActivator Activator { get; } = new();

    public WelcomeViewModel(IScreen hostScreen, ILocalizationService localizationService,
        NetworkProvider networkProvider)
        : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        Slides = new List<WelcomeSlide>
        {
            new()
            {
                Title = "AI powered safety",
                Description = "Your personal AI companion monitors and protects your mental wellbeing",
                Image = LoadImage("avares://Ecliptix.Core/Assets/DataSeed/safety.png")
            },
            new()
            {
                Title = "Mental Protection",
                Description = "Real-time content filtering and emotional support when you need it most",
                Image = LoadImage("avares://Ecliptix.Core/Assets/DataSeed/mentalprotection.png")
            },
            new()
            {
                Title = "Smart Communities",
                Description = "Connect with friends in verified, positive spaces designed for your safety",
                Image = LoadImage("avares://Ecliptix.Core/Assets/DataSeed/smartcommunities.png")
            },
            new()
            {
                Title = "Wellness First",
                Description = "Track your emotional health with insights and suggestions from our AI",
                Image = LoadImage("avares://Ecliptix.Core/Assets/DataSeed/wellness.png")
            }
        };



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

    private Bitmap? LoadImage(string path)
    {
        try
        {
            Uri uri = new Uri(path);
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string UrlPathSegment => "/welcome";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToCreateAccountCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToSignInCommand { get; }

    [ObservableAsProperty] public bool IsCreateAccountBusy { get; }

    [ObservableAsProperty] public bool IsSignInBusy { get; }

    public List<WelcomeSlide> Slides { get; }

    [Reactive]
    public int CurrentSlideIndex { get; set; }

    public void ResetState()
    {
        CurrentSlideIndex = 0;
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
