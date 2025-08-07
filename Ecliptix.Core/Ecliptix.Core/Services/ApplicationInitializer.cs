using Ecliptix.Core.Network;
using Ecliptix.Core.Security;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services;

public record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

public class ApplicationInitializer(
    NetworkProvider networkProvider,
    ISecureStorageProvider secureStorageProvider,
    ILocalizationService localizationService,
    ISystemEvents systemEvents,
    IHttpClientFactory httpClientFactory)
    : IApplicationInitializer
{
    private SecureStateStorage? _secureStateStorage;
    private IPlatformSecurityProvider? _platformSecurityProvider;
    
    public bool IsMembershipConfirmed { get; } = false;

    public async Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {
        systemEvents.Publish(SystemStateChangedEvent.New(SystemState.Initializing));

        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await secureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture);

        if (settingsResult.IsErr)
        {
            Log.Error("Failed to retrieve or create application instance settings: {@Error}",
                settingsResult.UnwrapErr());
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError));
            return false;
        }

        (ApplicationInstanceSettings settings, bool isNewInstance) = settingsResult.Unwrap();
        
        await InitializeSecureStorageAsync(settings);
        
        if (_secureStateStorage != null)
        {
            networkProvider.InitializeStatePersistence(_secureStateStorage);
        }

        _ = Task.Run(async () =>
        {
            await secureStorageProvider.SetApplicationInstanceAsync(isNewInstance);
        });
        
        localizationService.SetCulture(settings.Culture);

        _ = Task.Run(async () =>
        {
            Option<IpCountry> countryCode =
                await IpGeolocationService.GetIpCountryAsync(httpClientFactory, defaultSystemSettings.CountryCodeApi);

            if (countryCode.HasValue)
            {
                await secureStorageProvider.SetApplicationIpCountryAsync(countryCode.Value!);
            }
        });

        Result<uint, NetworkFailure> connectIdResult =
            await EnsureSecrecyChannelAsync(settings, isNewInstance);
        if (connectIdResult.IsErr)
        {
            Log.Error("Failed to establish or restore secrecy channel: {Error}", connectIdResult.UnwrapErr());
            return false;
        }

        uint connectId = connectIdResult.Unwrap();

        Result<Unit, NetworkFailure> registrationResult = await RegisterDeviceAsync(connectId, settings);
        if (registrationResult.IsErr)
        {
            Log.Error("Device registration failed: {Error}", registrationResult.UnwrapErr());
            return false;
        }

        Log.Information("Application initialized successfully");

        systemEvents.Publish(SystemStateChangedEvent.New(SystemState.Running));
        return true;
    }

    private async Task InitializeSecureStorageAsync(ApplicationInstanceSettings settings)
    {
        await Task.Run(() =>
        {
            try
            {
                string appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Ecliptix");
                
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }
                
                _platformSecurityProvider = new CrossPlatformSecurityProvider(appDataPath);
                
                string storagePath = Path.Combine(appDataPath, "protocol.state");
                byte[]? deviceId = settings.DeviceId.ToByteArray();
                
                _secureStateStorage = new SecureStateStorage(_platformSecurityProvider, storagePath, deviceId);
                
                if (_platformSecurityProvider.IsHardwareSecurityAvailable())
                {
                    Log.Information("Hardware security module detected and will be used for enhanced protection");
                }
                else
                {
                    Log.Information("Using software-based security with platform keychain");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize secure storage");
                throw;
            }
        });
    }

    private async Task<Result<uint, NetworkFailure>> EnsureSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings, bool isNewInstance)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

        if (!isNewInstance && _secureStateStorage != null)
        {
            try
            {
                string? userId = applicationInstanceSettings.AppInstanceId.ToStringUtf8();
                Result<byte[], SecureStorageFailure> loadResult = await _secureStateStorage.LoadStateAsync(userId);
                
                if (loadResult.IsOk)
                {
                    var stateBytes = loadResult.Unwrap();
                    EcliptixSecrecyChannelState? state = EcliptixSecrecyChannelState.Parser.ParseFrom(stateBytes);
                    
                    Result<bool, NetworkFailure> restoreSecrecyChannelResult =
                        await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings);

                    if (restoreSecrecyChannelResult.IsErr)
                        return Result<uint, NetworkFailure>.Err(restoreSecrecyChannelResult.UnwrapErr());
                        
                    if (restoreSecrecyChannelResult.IsOk && restoreSecrecyChannelResult.Unwrap())
                    {
                        Log.Information("Successfully restored and synchronized secrecy channel {ConnectId} from secure storage", connectId);
                        return Result<uint, NetworkFailure>.Ok(connectId);
                    }

                    Log.Warning("Failed to restore secrecy channel or it was out of sync. A new channel will be established");
                    networkProvider.ClearConnection(connectId);
                }
                else
                {
                    Log.Information("No saved protocol state found in secure storage");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load protocol state from secure storage, establishing new channel");
            }
        }

        networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);

        Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSecrecyChannelState secrecyChannelState = establishResult.Unwrap();
        
        if (_secureStateStorage != null)
        {
            try
            {
                string? userId = applicationInstanceSettings.AppInstanceId.ToStringUtf8();
                Result<Unit, SecureStorageFailure> saveResult = await _secureStateStorage.SaveStateAsync(
                    secrecyChannelState.ToByteArray(), 
                    userId);
                    
                if (saveResult.IsOk)
                {
                    Log.Information("Protocol state saved to secure storage for channel {ConnectId}", connectId);
                }
                else
                {
                    Log.Warning("Failed to save protocol state to secure storage: {Error}", saveResult.UnwrapErr());
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Exception while saving protocol state to secure storage");
            }
        }
        
        await secureStorageProvider.StoreAsync(connectId.ToString(), secrecyChannelState.ToByteArray());
        
        Log.Information("Successfully established new secrecy channel {ConnectId}", connectId);
        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task<Result<Unit, NetworkFailure>> RegisterDeviceAsync(uint connectId,
        ApplicationInstanceSettings settings)
    {
        AppDevice appDevice = new()
        {
            AppInstanceId = settings.AppInstanceId,
            DeviceId = settings.DeviceId,
            DeviceType = AppDevice.Types.DeviceType.Desktop
        };

        return await networkProvider.ExecuteServiceRequestAsync(
            connectId,
            RpcServiceType.RegisterAppDevice,
            appDevice.ToByteArray(),
            ServiceFlowType.Single,
            decryptedPayload =>
            {
                AppDeviceRegisteredStateReply reply =
                    Helpers.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);
                Guid appServerInstanceId = Helpers.FromByteStringToGuid(reply.UniqueId);

                settings.SystemDeviceIdentifier = appServerInstanceId.ToString();
                settings.ServerPublicKey = ByteString.CopyFrom(reply.ServerPublicKey.ToByteArray());

                Log.Information("Device successfully registered with server ID: {AppServerInstanceId}",
                    appServerInstanceId);
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, CancellationToken.None);
    }
}