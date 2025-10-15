using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Core.Communication;

namespace Ecliptix.Core.Features.Authentication;

public record AuthenticationModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.Eager,
    Dependencies: []
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
            ModuleName = Id.ToName()
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
