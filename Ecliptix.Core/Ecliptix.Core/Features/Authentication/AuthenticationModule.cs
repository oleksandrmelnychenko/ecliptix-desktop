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
using Serilog;

namespace Ecliptix.Core.Features.Authentication;

public record AuthenticationModuleManifest() : ModuleManifest(
    Version: new Version(1, 0, 0),
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.Eager,
    Dependencies: [],
    ResourceConstraints: ModuleResourceConstraints.Default
);

public class AuthenticationModule : ModuleBase<AuthenticationModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.Authentication;
    public override AuthenticationModuleManifest Manifest { get; } = new();

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