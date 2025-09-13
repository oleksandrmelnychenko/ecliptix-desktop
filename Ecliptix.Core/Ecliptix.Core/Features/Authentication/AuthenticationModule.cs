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
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.SignIn;
using Ecliptix.Core.Features.Authentication.Views.Registration;
using Ecliptix.Core.Features.Authentication.Views.Welcome;

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
        services.AddTransient<SecureKeyVerifierViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<MembershipHostWindowModel>();
    }

    public override IReadOnlyList<Type> GetViewTypes()
    {
        return
        [
            typeof(SignInView),
            typeof(MobileVerificationView),
            typeof(SecureKeyConfirmationView),
            typeof(PassPhaseView),
            typeof(WelcomeView)
        ];
    }

    public override IReadOnlyList<Type> GetViewModelTypes()
    {
        return
        [
            typeof(SignInViewModel),
            typeof(MobileVerificationViewModel),
            typeof(SecureKeyVerifierViewModel),
            typeof(PassPhaseViewModel),
            typeof(WelcomeViewModel)
        ];
    }

    public override void RegisterViewFactories(IModuleViewFactory viewFactory)
    {
        viewFactory.RegisterView<SignInViewModel, SignInView>();
        viewFactory.RegisterView<MobileVerificationViewModel, MobileVerificationView>();
        viewFactory.RegisterView<SecureKeyVerifierViewModel, SecureKeyConfirmationView>();
        viewFactory.RegisterView<PassPhaseViewModel, PassPhaseView>();
        viewFactory.RegisterView<WelcomeViewModel, WelcomeView>();

        Serilog.Log.Information("Registered {Count} view factories for Authentication module", 5);
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

    private async Task OnUserAuthenticated(UserAuthenticatedEvent authEvent)
    {
        Logger?.LogInformation("User authenticated: {UserId} - {Username}",
            authEvent.UserId, authEvent.Username);
        await Task.CompletedTask;
    }

    private async Task OnUserSignedOut(UserSignedOutEvent signOutEvent)
    {
        Logger?.LogInformation("User signed out: {UserId}", signOutEvent.UserId);
        await Task.CompletedTask;
    }

    private async Task OnNavigationRequested(NavigationRequestedEvent navEvent)
    {
        if (navEvent.TargetView == "Authentication")
        {
            Logger?.LogDebug("Navigation to authentication view requested from: {SourceModule}",
                navEvent.SourceModule);
        }
        await Task.CompletedTask;
    }
}