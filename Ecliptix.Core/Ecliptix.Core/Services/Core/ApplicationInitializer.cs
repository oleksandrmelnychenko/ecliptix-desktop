using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

public class ApplicationInitializer(
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    ILocalizationService localizationService,
    ISystemEvents systemEvents,
    IIpGeolocationService ipGeolocationService)
    : IApplicationInitializer
{
    public bool IsMembershipConfirmed { get; } = false;

    public async Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {
        systemEvents.Publish(SystemStateChangedEvent.New(SystemState.Initializing));

        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture);

        if (settingsResult.IsErr)
        {
            Log.Error("Failed to retrieve or create application instance settings: {@Error}",
                settingsResult.UnwrapErr());
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError));
            return false;
        }

        (ApplicationInstanceSettings settings, bool isNewInstance) = settingsResult.Unwrap();

        _ = Task.Run(async () =>
        {
            await applicationSecureStorageProvider.SetApplicationInstanceAsync(isNewInstance);
        });

        localizationService.SetCulture(settings.Culture);

        if (isNewInstance)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    Result<IpCountry, InternalServiceApiFailure> countryResult =
                        await ipGeolocationService.GetIpCountryAsync(CancellationToken.None);

                    if (countryResult.IsOk)
                    {

                        networkProvider.SetCountry(countryResult.Unwrap().Country);
                        await applicationSecureStorageProvider.SetApplicationIpCountryAsync(countryResult.Unwrap());
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to get IP country information");
                }
            });
        }
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
    private async Task<Result<uint, NetworkFailure>> EnsureSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings, bool isNewInstance)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings, PubKeyExchangeType.DataCenterEphemeralConnect);

        if (!isNewInstance)
        {
            try
            {
                //await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

                Result<byte[], SecureStorageFailure> loadResult =
                    await secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

                if (loadResult.IsOk)
                {
                    byte[] stateBytes = loadResult.Unwrap();
                    EcliptixSecrecyChannelState? state = EcliptixSecrecyChannelState.Parser.ParseFrom(stateBytes);

                    Result<bool, NetworkFailure> restoreSecrecyChannelResult =
                        await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings);

                    if (restoreSecrecyChannelResult.IsErr)
                        return Result<uint, NetworkFailure>.Err(restoreSecrecyChannelResult.UnwrapErr());

                    if (restoreSecrecyChannelResult.IsOk && restoreSecrecyChannelResult.Unwrap())
                    {
                        Log.Information(
                            "Successfully restored and synchronized secrecy channel {ConnectId} from secure storage",
                            connectId);
                        return Result<uint, NetworkFailure>.Ok(connectId);
                    }

                    Log.Warning(
                        "Failed to restore secrecy channel or it was out of sync. A new channel will be established");
                    networkProvider.ClearConnection(connectId);
                    await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
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

        try
        {
            Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                secrecyChannelState.ToByteString(),
                span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

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

        return await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegisterAppDevice,
            SecureByteStringInterop.WithByteStringAsSpan(appDevice.ToByteString(),
                span => span.ToArray()),
            decryptedPayload =>
            {
                AppDeviceRegisteredStateReply reply =
                    Helpers.ParseFromBytes<AppDeviceRegisteredStateReply>(decryptedPayload);
                Guid appServerInstanceId = Helpers.FromByteStringToGuid(reply.UniqueId);

                settings.SystemDeviceIdentifier = appServerInstanceId.ToString();
                settings.ServerPublicKey = SecureByteStringInterop.WithByteStringAsSpan(reply.ServerPublicKey,
                    ByteString.CopyFrom);

                Log.Information("Device successfully registered with server ID: {AppServerInstanceId}",
                    appServerInstanceId);
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None);
    }
}