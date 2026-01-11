using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;

namespace Ecliptix.Feature.Authentication.ViewModels.Hosts;

public readonly struct ViewModelFactoryContext
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IGlobalModalService GlobalModalService { get; init; }
    public required IAuthRepository AuthRepository { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required AuthenticationViewModel HostViewModel { get; init; }
    public required AuthenticationFlowContext FlowContext { get; init; }
    public required DefaultSystemSettings Settings { get; init; }
    public required IMessageBus MessageBus { get; init; }
}
