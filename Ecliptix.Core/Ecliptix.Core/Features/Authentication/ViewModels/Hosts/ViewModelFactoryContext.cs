using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

public readonly struct ViewModelFactoryContext
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IAuthenticationService AuthenticationService { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required AuthenticationViewModel HostViewModel { get; init; }
    public required IOpaqueRegistrationService RegistrationService { get; init; }
    public required ISecureKeyRecoveryService RecoveryService { get; init; }
    public required AuthenticationFlowContext FlowContext { get; init; }
}
