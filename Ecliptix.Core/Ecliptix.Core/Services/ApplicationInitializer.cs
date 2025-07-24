using Ecliptix.Core.Network;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;
using Splat;

namespace Ecliptix.Core.Services;

public record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

public class ApplicationInitializer(
    NetworkProvider networkProvider,
    ISecureStorageProvider secureStorageProvider,
    ILocalizationService localizationService,
    ISystemEvents systemEvents)
    : IApplicationInitializer
{
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

        localizationService.SetCulture(settings.Culture);
        
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
            NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

        if (!isNewInstance)
        {
            Result<Option<byte[]>, InternalServiceApiFailure> storedStateResult =
                await secureStorageProvider.TryGetByKeyAsync(connectId.ToString());
            if (storedStateResult.IsOk && storedStateResult.Unwrap().HasValue)
            {
                EcliptixSecrecyChannelState? state =
                    EcliptixSecrecyChannelState.Parser.ParseFrom(storedStateResult.Unwrap().Value);
                Result<bool, NetworkFailure> restoreSecrecyChannelResult =
                    await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings);

                if (restoreSecrecyChannelResult.IsErr)
                    return Result<uint, NetworkFailure>.Err(restoreSecrecyChannelResult.UnwrapErr());
                if (restoreSecrecyChannelResult.IsOk && restoreSecrecyChannelResult.Unwrap())
                {
                    Log.Information("Successfully restored and synchronized secrecy channel {ConnectId}", connectId);
                    return Result<uint, NetworkFailure>.Ok(connectId);
                }

                Log.Warning(
                    "Failed to restore secrecy channel or it was out of sync. A new channel will be established");
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
            RcpServiceType.RegisterAppDevice,
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