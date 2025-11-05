using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Network;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

public sealed class ApplicationInitializer(
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    ILocalizationService localizationService,
    IIpGeolocationService ipGeolocationService,
    IIdentityService identityService,
    IApplicationStateManager stateManager,
    IStateCleanupService stateCleanupService) : IApplicationInitializer
{
    private const int IP_GEOLOCATION_TIMEOUT_SECONDS = 10;

    private readonly PendingLogoutProcessor _pendingLogoutProcessor = new(
        networkProvider,
        new PendingLogoutRequestStorage(applicationSecureStorageProvider));

    public async Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {
        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture)
                .ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return false;
        }

        (ApplicationInstanceSettings settings, bool isNewInstance) = settingsResult.Unwrap();

        _ = Task.Run(async () =>
        {
            await applicationSecureStorageProvider.SetApplicationInstanceAsync(isNewInstance).ConfigureAwait(false);
        });

        string culture = string.IsNullOrEmpty(settings.Culture)
            ? AppCultureSettingsConstants.DEFAULT_CULTURE_CODE
            : settings.Culture;
        localizationService.SetCulture(culture);

        if (isNewInstance)
        {
            _ = FetchIpGeolocationInBackgroundAsync().ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Serilog.Log.Error(task.Exception,
                            "[APPLICATION-INITIALIZER] Unhandled exception fetching IP geolocation");
                    }
                },
                TaskScheduler.Default);
        }

        Result<uint, NetworkFailure> connectIdResult =
            await EnsureSecrecyChannelAsync(settings, isNewInstance).ConfigureAwait(false);
        if (connectIdResult.IsErr)
        {
            return false;
        }

        uint connectId = connectIdResult.Unwrap();

        Result<Unit, NetworkFailure> registrationResult =
            await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);
        if (registrationResult.IsErr)
        {
            Log.Error("[CLIENT-REGISTER] RegisterDevice failed. ConnectId: {ConnectId}, ERROR: {ERROR}",
                connectId, registrationResult.UnwrapErr().Message);
            return false;
        }

        await ProcessPendingLogoutRequestsAsync(connectId).ConfigureAwait(false);

        return true;
    }

    private async Task ProcessPendingLogoutRequestsAsync(uint connectId) =>
        await _pendingLogoutProcessor.ProcessPendingLogoutAsync(connectId).ConfigureAwait(false);

    private Task FetchIpGeolocationInBackgroundAsync() =>
        Task.Run(async () =>
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(IP_GEOLOCATION_TIMEOUT_SECONDS));
            Result<IpCountry, InternalServiceApiFailure> countryResult =
                await ipGeolocationService.GetIpCountryAsync(cts.Token).ConfigureAwait(false);

            if (countryResult.IsOk)
            {
                IpCountry country = countryResult.Unwrap();
                networkProvider.SetCountry(country.Country);
                await applicationSecureStorageProvider.SetApplicationIpCountryAsync(country).ConfigureAwait(false);
            }
        });

    private async Task<Result<uint, NetworkFailure>> EnsureSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings, bool isNewInstance)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

        string? membershipId = ExtractMembershipId(applicationInstanceSettings);

        if (!isNewInstance)
        {
            Result<uint, NetworkFailure>? restoreResult =
                await TryRestoreExistingSessionAsync(connectId, applicationInstanceSettings, membershipId)
                    .ConfigureAwait(false);

            if (restoreResult.HasValue)
            {
                return restoreResult.Value;
            }
        }

        return await EstablishNewSecrecyChannelAsync(applicationInstanceSettings, connectId, membershipId)
            .ConfigureAwait(false);
    }

    private static string? ExtractMembershipId(ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (applicationInstanceSettings.Membership?.UniqueIdentifier is { IsEmpty: false })
        {
            return Helpers.FromByteStringToGuid(applicationInstanceSettings.Membership.UniqueIdentifier).ToString();
        }

        return null;
    }

    private async Task<Result<uint, NetworkFailure>?> TryRestoreExistingSessionAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings,
        string? membershipId)
    {
        Result<bool, NetworkFailure> restoreResult =
            await TryRestoreSessionStateAsync(connectId, applicationInstanceSettings).ConfigureAwait(false);

        if (restoreResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Err(restoreResult.UnwrapErr());
        }

        if (!restoreResult.Unwrap())
        {
            return null;
        }

        if (!string.IsNullOrEmpty(membershipId) &&
            await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false))
        {
            await stateManager.TransitionToAuthenticatedAsync(membershipId).ConfigureAwait(false);
        }

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishNewSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId,
        string? membershipId)
    {
        Option<SodiumSecureMemoryHandle> masterKeyHandle =
            await PrepareMasterKeyHandleAsync(membershipId, applicationInstanceSettings)
                .ConfigureAwait(false);

        try
        {
            bool shouldUseAuthenticatedProtocol = masterKeyHandle.IsSome;

            if (shouldUseAuthenticatedProtocol)
            {
                Result<uint, NetworkFailure>? authenticatedResult =
                    await TryUseAuthenticatedProtocolAsync(
                            applicationInstanceSettings,
                            connectId,
                            membershipId!,
                            masterKeyHandle.Value!)
                        .ConfigureAwait(false);

                if (authenticatedResult.HasValue)
                {
                    return authenticatedResult.Value;
                }
            }

            await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
                .ConfigureAwait(false);

            byte[]? membershipIdBytes = applicationInstanceSettings.Membership?.UniqueIdentifier?.ToByteArray();
            return await EstablishAndSaveSecrecyChannelAsync(connectId, membershipIdBytes).ConfigureAwait(false);
        }
        finally
        {
            masterKeyHandle.Do(handle => handle.Dispose());
        }
    }

    private async Task<Option<SodiumSecureMemoryHandle>> PrepareMasterKeyHandleAsync(
        string? membershipId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (string.IsNullOrEmpty(membershipId))
        {
            return Option<SodiumSecureMemoryHandle>.None;
        }

        bool hasStoredIdentity = await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false);
        if (!hasStoredIdentity)
        {
            return Option<SodiumSecureMemoryHandle>.None;
        }

        return await TryReconstructMasterKeyAsync(membershipId, applicationInstanceSettings)
            .ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>?> TryUseAuthenticatedProtocolAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId,
        string membershipId,
        SodiumSecureMemoryHandle masterKeyHandle)
    {
        if (applicationInstanceSettings.Membership?.UniqueIdentifier == null)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(
                    "Membership information is missing for authenticated protocol"));
        }

        ByteString membershipByteString = applicationInstanceSettings.Membership.UniqueIdentifier;

        Result<Unit, NetworkFailure> recreateResult =
            await networkProvider.RecreateProtocolWithMasterKeyAsync(
                masterKeyHandle, membershipByteString, connectId).ConfigureAwait(false);

        if (recreateResult.IsErr)
        {
            await HandleAuthenticatedProtocolFailureAsync(recreateResult.UnwrapErr(), membershipId,
                    applicationInstanceSettings, connectId)
                .ConfigureAwait(false);
            return null;
        }

        await stateManager.TransitionToAuthenticatedAsync(membershipId).ConfigureAwait(false);
        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task HandleAuthenticatedProtocolFailureAsync(
        NetworkFailure failure,
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId)
    {
        if (failure.FailureType == NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE)
        {
            await CleanupCorruptedIdentityDataAsync(membershipId, applicationInstanceSettings)
                .ConfigureAwait(false);
        }

        await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
            .ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishAndSaveSecrecyChannelAsync(uint connectId,
        byte[]? membershipId)
    {
        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        if (membershipId != null)
        {
            await SecureByteStringInterop.WithByteStringAsSpan(
                    secrecyChannelState.ToByteString(),
                    span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(),
                        membershipId))
                .ConfigureAwait(false);
        }

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task InitializeProtocolWithoutIdentityAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId)
    {
        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

        if (applicationInstanceSettings.Membership != null)
        {
            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);

            applicationInstanceSettings.Membership = null;
        }

        networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
    }

    private async Task<Result<bool, NetworkFailure>> TryRestoreSessionStateAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        byte[]? membershipId = applicationInstanceSettings.Membership?.UniqueIdentifier?.ToByteArray();
        if (membershipId == null)
        {
            return Result<bool, NetworkFailure>.Ok(false);
        }

        Result<byte[], SecureStorageFailure> loadResult =
            await secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);

        if (loadResult.IsErr)
        {
            return Result<bool, NetworkFailure>.Ok(false);
        }

        byte[] stateBytes = loadResult.Unwrap();

        EcliptixSessionState? state;
        try
        {
            state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
        }
        catch (InvalidProtocolBufferException ex)
        {
            networkProvider.ClearConnection(connectId);
            Result<Unit, SecureStorageFailure> deleteSecureStateResult =
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
            if (deleteSecureStateResult.IsErr)
            {
                Log.Warning(ex,
                    "[CLIENT-RESTORE-CLEANUP] Failed to delete corrupted state. ConnectId: {ConnectId}, ERROR: {ERROR}",
                    connectId, deleteSecureStateResult.UnwrapErr().Message);
            }

            return Result<bool, NetworkFailure>.Ok(false);
        }

        string membershipIdString = SecureByteStringInterop.WithByteStringAsSpan(
            applicationInstanceSettings.Membership!.UniqueIdentifier!,
            span => new Guid(span.ToArray()).ToString());

        bool hasRevocationProof = await LogoutService.HasRevocationProofAsync(
            applicationSecureStorageProvider,
            membershipIdString).ConfigureAwait(false);

        if (hasRevocationProof)
        {
            networkProvider.ClearConnection(connectId);
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

            return Result<bool, NetworkFailure>.Ok(false);
        }

        Result<bool, NetworkFailure> restoreResult =
            await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings).ConfigureAwait(false);

        if (restoreResult.IsErr)
        {
            networkProvider.ClearConnection(connectId);
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

            return Result<bool, NetworkFailure>.Ok(false);
        }

        if (restoreResult.Unwrap())
        {
            return Result<bool, NetworkFailure>.Ok(true);
        }

        networkProvider.ClearConnection(connectId);
        await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        return Result<bool, NetworkFailure>.Ok(false);
    }

    private async Task<Option<SodiumSecureMemoryHandle>> TryLoadMasterKeyFromStorageAsync(string membershipId)
    {
        Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult =
            await identityService.LoadMasterKeyHandleAsync(membershipId).ConfigureAwait(false);

        if (loadResult.IsErr)
        {
            return Option<SodiumSecureMemoryHandle>.None;
        }

        SodiumSecureMemoryHandle loadedHandle = loadResult.Unwrap();

        Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
            loadedHandle.ReadBytes(loadedHandle.Length);
        if (!readResult.IsOk)
        {
            return Option<SodiumSecureMemoryHandle>.Some(loadedHandle);
        }

        byte[] masterKeyBytes = readResult.Unwrap();
        CryptographicOperations.ZeroMemory(masterKeyBytes);

        return Option<SodiumSecureMemoryHandle>.Some(loadedHandle);
    }

    private async Task<Option<SodiumSecureMemoryHandle>> TryReconstructMasterKeyAsync(
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        Option<SodiumSecureMemoryHandle> storageHandle =
            await TryLoadMasterKeyFromStorageAsync(membershipId).ConfigureAwait(false);

        if (storageHandle.IsSome)
        {
            return storageHandle;
        }

        await CleanupCorruptedIdentityDataAsync(membershipId, applicationInstanceSettings).ConfigureAwait(false);

        return Option<SodiumSecureMemoryHandle>.None;
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
                DeviceRegistrationResponse reply =
                    Helpers.ParseFromBytes<DeviceRegistrationResponse>(decryptedPayload);

                settings.ServerPublicKey = SecureByteStringInterop.WithByteStringAsSpan(reply.ServerPublicKey,
                    ByteString.CopyFrom);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CleanupCorruptedIdentityDataAsync(
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        try
        {
            uint connectId = NetworkProvider.ComputeUniqueConnectId(
                applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

            Result<Unit, Exception> cleanupResult =
                await stateCleanupService.CleanupMembershipStateWithKeysAsync(membershipId, connectId)
                    .ConfigureAwait(false);

            if (cleanupResult.IsErr)
            {
                return;
            }

            await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-RECOVERY] Failed to cleanup corrupted identity data. MembershipId: {MembershipId}",
                membershipId);
        }
    }
}
