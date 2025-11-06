using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels.Core;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

public readonly struct AuthenticationViewModelDependencies
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required IAuthenticationService AuthenticationService { get; init; }
    public required IOpaqueRegistrationService RegistrationService { get; init; }
    public required ISecureKeyRecoveryService RecoveryService { get; init; }
    public required ILanguageDetectionService LanguageDetectionService { get; init; }
    public required IApplicationRouter Router { get; init; }
    public required MainWindowViewModel MainWindowViewModel { get; init; }
    public required DefaultSystemSettings Settings { get; init; }
}
