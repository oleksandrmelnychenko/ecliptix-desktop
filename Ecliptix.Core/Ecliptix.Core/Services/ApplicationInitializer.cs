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
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services;

public class ApplicationInitializer(
    NetworkProvider networkProvider,
    ISecureStorageProvider secureStorageProvider,
    ISystemEvents systemEvents)
    : IApplicationInitializer
{
    private record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

    public async Task<bool> InitializeAsync()
    {
        systemEvents.Publish(SystemStateChangedEvent.New(SystemState.Initializing));

        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await GetOrCreateInstanceSettingsAsync();
        if (settingsResult.IsErr)
        {
            Log.Error("Failed to get or create application instance settings: {Error}", settingsResult.UnwrapErr());
            return false;
        }

        (ApplicationInstanceSettings settings, bool isNewInstance) = settingsResult.Unwrap();

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
        return true;
    }

    public bool IsMembershipConfirmed { get; } = false;

    private async Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> GetOrCreateInstanceSettingsAsync()
    {
        const string settingsKey = "ApplicationInstanceSettings";
        Result<Option<byte[]>, InternalServiceApiFailure> getResult =
            await secureStorageProvider.TryGetByKeyAsync(settingsKey);

        if (getResult.IsErr)
        {
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Err(getResult.UnwrapErr());
        }

        Option<byte[]> maybeSettingsData = getResult.Unwrap();

        if (maybeSettingsData.HasValue)
        {
            ApplicationInstanceSettings? existingSettings =
                ApplicationInstanceSettings.Parser.ParseFrom(maybeSettingsData.Value);
            return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
                new InstanceSettingsResult(existingSettings, false));
        }

        ApplicationInstanceSettings newSettings = new()
        {
            AppInstanceId = Helpers.GuidToByteString(Guid.NewGuid()),
            DeviceId = Helpers.GuidToByteString(Guid.NewGuid()),
            Culture = "en-US",
        };

        await secureStorageProvider.StoreAsync(settingsKey, newSettings.ToByteArray());
        return Result<InstanceSettingsResult, InternalServiceApiFailure>.Ok(
            new InstanceSettingsResult(newSettings, true));
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
                    await networkProvider.RestoreSecrecyChannel(state, applicationInstanceSettings);

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
            await networkProvider.EstablishSecrecyChannel(connectId);

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

        return await networkProvider.ExecuteServiceRequest(
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