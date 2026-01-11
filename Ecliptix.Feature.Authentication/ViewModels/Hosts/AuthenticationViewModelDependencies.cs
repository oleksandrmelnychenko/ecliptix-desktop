using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.ViewModels;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;

namespace Ecliptix.Feature.Authentication.ViewModels.Hosts;

public readonly struct AuthenticationViewModelDependencies
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required ILanguageDetectionService LanguageDetectionService { get; init; }
    public required IApplicationRouter Router { get; init; }
    public required IGlobalModalService GlobalModalService { get; init; }
    public required MainWindowViewModel MainWindowViewModel { get; init; }
    public required DefaultSystemSettings Settings { get; init; }
    public required IMessageBus MessageBus { get; init; }
    public required IAuthRepository AuthRepository { get; init; }
}
