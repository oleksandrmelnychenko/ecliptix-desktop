using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using Ecliptix.Core.Features.Authentication.Views.Hosts;
using Ecliptix.Core.Features.Authentication.Services;

namespace Ecliptix.Core.Features.Authentication;

public class AuthenticationModule : ModuleBase
{
    public override string Name => "Authentication";
    public override int Priority => 30;
    public override ModuleLoadingStrategy LoadingStrategy => ModuleLoadingStrategy.Eager;

    public override IReadOnlyList<string> DependsOn => [];

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<SignInViewModel>();
        services.AddTransient<MobileVerificationViewModel>();
        services.AddTransient<PasswordConfirmationViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<MembershipHostWindowModel>();
    }

    public override void RegisterViews(IViewLocator viewLocator)
    {
        // Only register IRoutableViewModel types (child ViewModels that are navigated to)
        viewLocator.Register(typeof(SignInViewModel), typeof(SignInView));
        viewLocator.Register(typeof(MobileVerificationViewModel), typeof(MobileVerificationView));
        viewLocator.Register(typeof(PasswordConfirmationViewModel), typeof(PasswordConfirmationView));
        viewLocator.Register(typeof(PassPhaseViewModel), typeof(PassPhaseView));
        viewLocator.Register(typeof(WelcomeViewModel), typeof(WelcomeView));
        // Note: MembershipHostWindowModel is IScreen, not IRoutableViewModel, so it's not registered here
    }
    
    public override IReadOnlyList<Type> GetViewTypes()
    {
        return
        [
            typeof(SignInView),
            typeof(MobileVerificationView),
            typeof(PasswordConfirmationView),
            typeof(PassPhaseView),
            typeof(WelcomeView)
            // Note: MembershipHostWindow is a Window, not a navigable View within the router
        ];
    }
    
    public override IReadOnlyList<Type> GetViewModelTypes()
    {
        return
        [
            typeof(SignInViewModel),
            typeof(MobileVerificationViewModel),
            typeof(PasswordConfirmationViewModel),
            typeof(PassPhaseViewModel),
            typeof(WelcomeViewModel)
            // Note: MembershipHostWindowModel is IScreen, not IRoutableViewModel
        ];
    }

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        messageBus.Subscribe<UserAuthenticatedEvent>(OnUserAuthenticated);
        messageBus.Subscribe<UserSignedOutEvent>(OnUserSignedOut);
        messageBus.Subscribe<NavigationRequestedEvent>(OnNavigationRequested);
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            SourceModule = Name,
            ModuleName = Name,
            ModuleVersion = "1.0.0"
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