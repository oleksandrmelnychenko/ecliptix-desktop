using Ecliptix.Core.Modularity;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Feature.Authentication.ViewModels.Registration;
using Ecliptix.Feature.Authentication.ViewModels.SignIn;
using Ecliptix.Feature.Authentication.ViewModels.Welcome;
using Ecliptix.Feature.Authentication.Views.Hosts;
using Ecliptix.Feature.Authentication.Views.Registration;
using Ecliptix.Feature.Authentication.Views.SignIn;
using Ecliptix.Feature.Authentication.Views.Welcome;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Authentication;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<AuthenticationModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthRepository, AuthRepository>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<AuthenticationViewModel, AuthenticationView>();
        factory.RegisterView<WelcomeViewModel, WelcomeView>();
        factory.RegisterView<WelcomeBackViewModel, WelcomeBackView>();
        factory.RegisterView<SignInViewModel, SignInView>();
        factory.RegisterView<MobileVerificationViewModel, MobileVerificationView>();
        factory.RegisterView<VerificationCodeEntryViewModel, VerificationCodeEntryView>();
        factory.RegisterView<SecureKeyConfirmationViewModel, SecureKeyConfirmationView>();
        factory.RegisterView<CompleteProfileViewModel, CompleteProfileView>();
        factory.RegisterView<PassPhaseViewModel, PassPhaseView>();
    }
}
