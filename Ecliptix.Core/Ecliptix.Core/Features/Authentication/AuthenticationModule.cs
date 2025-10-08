using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Features.Authentication.ViewModels.PasswordRecovery;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.SignIn;
using Ecliptix.Core.Features.Authentication.Views.Registration;
using Ecliptix.Core.Features.Authentication.Views.Welcome;
using Ecliptix.Core.Features.Authentication.Views.PasswordRecovery;

namespace Ecliptix.Core.Features.Authentication;

public record AuthenticationModuleManifest() : ModuleManifest(
    Id: ModuleIdentifier.Authentication,
    DisplayName: "Authentication Module",
    Version: new Version(1, 0, 0),
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.Eager,
    Dependencies: [],
    ResourceConstraints: ModuleResourceConstraints.Default,
    ViewFactories: new Dictionary<Type, Func<Control>>(),
    ServiceMappings: new Dictionary<Type, Type>()
);

public class AuthenticationModule : ModuleBase<AuthenticationModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.Authentication;
    public override AuthenticationModuleManifest Manifest { get; } = new();

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<SignInViewModel>();
        services.AddTransient<MobileVerificationViewModel>();
        services.AddTransient<VerifyOtpViewModel>();
        services.AddTransient<SecureKeyVerifierViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<ForgotPasswordResetViewModel>();
        services.AddTransient<MembershipHostWindowModel>();
    }

    public override IReadOnlyList<Type> GetViewTypes()
    {
        return
        [
            typeof(SignInView),
            typeof(MobileVerificationView),
            typeof(VerificationCodeEntryView),
            typeof(SecureKeyConfirmationView),
            typeof(PassPhaseView),
            typeof(WelcomeView),
            typeof(ForgotPasswordResetView)
        ];
    }

    public override IReadOnlyList<Type> GetViewModelTypes()
    {
        return
        [
            typeof(SignInViewModel),
            typeof(MobileVerificationViewModel),
            typeof(VerifyOtpViewModel),
            typeof(SecureKeyVerifierViewModel),
            typeof(PassPhaseViewModel),
            typeof(WelcomeViewModel),
            typeof(ForgotPasswordResetViewModel)
        ];
    }

    public override void RegisterViewFactories(IModuleViewFactory viewFactory)
    {
        viewFactory.RegisterView<SignInViewModel, SignInView>();
        viewFactory.RegisterView<MobileVerificationViewModel, MobileVerificationView>();
        viewFactory.RegisterView<VerifyOtpViewModel, VerificationCodeEntryView>();
        viewFactory.RegisterView<SecureKeyVerifierViewModel, SecureKeyConfirmationView>();
        viewFactory.RegisterView<PassPhaseViewModel, PassPhaseView>();
        viewFactory.RegisterView<WelcomeViewModel, WelcomeView>();
        viewFactory.RegisterView<ForgotPasswordResetViewModel, ForgotPasswordResetView>();

        Serilog.Log.Information("Registered {Count} view factories for Authentication module", 7);
    }

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        messageBus.Subscribe<UserAuthenticatedEvent>(OnUserAuthenticated);
        messageBus.Subscribe<UserSignedOutEvent>(OnUserSignedOut);
        messageBus.Subscribe<NavigationRequestedEvent>(OnNavigationRequested);
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            SourceModule = Id.ToName(),
            ModuleName = Id.ToName(),
            ModuleVersion = Manifest.Version.ToString()
        });

        Logger?.LogInformation("Authentication module message handlers setup completed");
    }

    private Task OnUserAuthenticated(UserAuthenticatedEvent authEvent)
    {
        Logger?.LogInformation("User authenticated: {UserId} - {Username}",
            authEvent.UserId, authEvent.Username);
        return Task.CompletedTask;
    }

    private Task OnUserSignedOut(UserSignedOutEvent signOutEvent)
    {
        Logger?.LogInformation("User signed out: {UserId}", signOutEvent.UserId);
        return Task.CompletedTask;
    }

    private Task OnNavigationRequested(NavigationRequestedEvent navEvent)
    {
        if (navEvent.TargetView == "Authentication")
        {
            Logger?.LogDebug("Navigation to authentication view requested from: {SourceModule}",
                navEvent.SourceModule);
        }
        return Task.CompletedTask;
    }
}