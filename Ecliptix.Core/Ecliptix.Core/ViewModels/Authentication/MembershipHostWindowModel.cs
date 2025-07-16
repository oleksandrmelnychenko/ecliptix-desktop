using System;
using Avalonia.Media;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class MembershipHostWindowModel : ReactiveObject, IScreen, IDisposable
{
    private readonly IDisposable _colorSubscription;
    private string _backgroundColor = "#e8e9ff";

    public string BackgroundColor
    {
        get => _backgroundColor;
        private set => this.RaiseAndSetIfChanged(ref _backgroundColor, value);
    }

    public RoutingState Router { get; } = new RoutingState();

    public ReactiveCommand<AuthViewType, IRoutableViewModel> Navigate { get; }

    public MembershipHostWindowModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService
    )
    {
        Navigate = ReactiveCommand.CreateFromObservable<AuthViewType, IRoutableViewModel>(
            viewType =>
                Router.Navigate.Execute(
                    CreateViewModelForView(viewType, networkProvider, localizationService)!
                )
        );

        Navigate.Execute(AuthViewType.MembershipWelcome).Subscribe();

        _colorSubscription = MessageBus
            .Current.Listen<BackgroundColorChangedMessage>()
            .Subscribe(msg =>
            {
                Color toColor = Color.Parse(msg.ColorHex);
                byte opacity = (byte)(255 * msg.Opacity);
                toColor = Color.FromArgb(opacity, toColor.R, toColor.G, toColor.B);

                BackgroundColor = toColor.ToString();
            });
    }

    private IRoutableViewModel CreateViewModelForView(
        AuthViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService
    )
    {
        return viewType switch
        {
            AuthViewType.SignIn => new SignInViewModel(networkProvider, localizationService, this),
            AuthViewType.MembershipWelcome => new WelcomeViewModel(this),
            //AuthViewType.RegistrationWizard => dependencyResolver.GetRequiredService<RegistrationWizardViewModel>(),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType)),
        };
    }

    public void Dispose()
    {
        _colorSubscription?.Dispose();
    }
}
