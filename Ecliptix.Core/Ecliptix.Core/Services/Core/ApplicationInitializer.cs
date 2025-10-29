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
    #region Constants & Fields

    private const int IpGeolocationTimeoutSeconds = 10;

    private readonly PendingLogoutProcessor _pendingLogoutProcessor = new(
        networkProvider,
        new PendingLogoutRequestStorage(applicationSecureStorageProvider));

    #endregion

    #region Public API

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
            ? AppCultureSettingsConstants.DefaultCultureCode
            : settings.Culture;
        localizationService.SetCulture(culture);

        if (isNewInstance)
        {
            _ = FetchIpGeolocationInBackgroundAsync();
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
            Log.Error("[CLIENT-REGISTER] RegisterDevice failed. ConnectId: {ConnectId}, Error: {Error}",
                connectId, registrationResult.UnwrapErr().Message);
            return false;
        }

        await ProcessPendingLogoutRequestsAsync(connectId).ConfigureAwait(false);

        return true;
    }

    #endregion

    #region Background Tasks

    private async Task ProcessPendingLogoutRequestsAsync(uint connectId)
    {
        try
        {
            await _pendingLogoutProcessor.ProcessPendingLogoutAsync(connectId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[INIT-PENDING-LOGOUT] Failed to process pending logout requests");
        }
    }

    private Task FetchIpGeolocationInBackgroundAsync() =>
        Task.Run(async () =>
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(IpGeolocationTimeoutSeconds));
            Result<IpCountry, InternalServiceApiFailure> countryResult =
                await ipGeolocationService.GetIpCountryAsync(cts.Token).ConfigureAwait(false);

            if (countryResult.IsOk)
            {
                IpCountry country = countryResult.Unwrap();
                networkProvider.SetCountry(country.Country);
                await applicationSecureStorageProvider.SetApplicationIpCountryAsync(country).ConfigureAwait(false);
            }
        });

    #endregion

    #region Protocol & Channel Establishment

    private async Task<Result<uint, NetworkFailure>> EnsureSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings, bool isNewInstance)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

        string? membershipId = applicationInstanceSettings.Membership?.UniqueIdentifier?.IsEmpty == false
            ? Helpers.FromByteStringToGuid(applicationInstanceSettings.Membership.UniqueIdentifier).ToString()
            : null;

        if (!isNewInstance)
        {
            Result<bool, NetworkFailure> restoreResult =
                await TryRestoreSessionStateAsync(connectId, applicationInstanceSettings).ConfigureAwait(false);

            if (restoreResult.IsErr)
            {
                return Result<uint, NetworkFailure>.Err(restoreResult.UnwrapErr());
            }

            if (restoreResult.Unwrap())
            {
                if (!string.IsNullOrEmpty(membershipId) &&
                    await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false))
                {
                    await stateManager.TransitionToAuthenticatedAsync(membershipId).ConfigureAwait(false);
                }

                return Result<uint, NetworkFailure>.Ok(connectId);
            }
        }

        bool shouldUseAuthenticatedProtocol = false;
        Option<SodiumSecureMemoryHandle> masterKeyHandle = Option<SodiumSecureMemoryHandle>.None;

        bool hasStoredIdentity = !string.IsNullOrEmpty(membershipId) &&
                                 await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false);
        if (hasStoredIdentity)
        {
            masterKeyHandle = await TryReconstructMasterKeyAsync(membershipId!, applicationInstanceSettings)
                .ConfigureAwait(false);

            if (masterKeyHandle.IsSome)
            {
                shouldUseAuthenticatedProtocol = true;
            }
        }

        try
        {
            if (shouldUseAuthenticatedProtocol && masterKeyHandle.IsSome)
            {
                ByteString membershipByteString = applicationInstanceSettings.Membership!.UniqueIdentifier!;

                Result<Unit, NetworkFailure> recreateResult =
                    await networkProvider.RecreateProtocolWithMasterKeyAsync(
                        masterKeyHandle.Value!, membershipByteString, connectId).ConfigureAwait(false);

                if (recreateResult.IsErr)
                {
                    NetworkFailure failure = recreateResult.UnwrapErr();

                    if (failure.FailureType == NetworkFailureType.CriticalAuthenticationFailure)
                    {
                        await CleanupCorruptedIdentityDataAsync(membershipId!, applicationInstanceSettings)
                            .ConfigureAwait(false);
                    }

                    await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
                        .ConfigureAwait(false);
                }
                else
                {
                    await stateManager.TransitionToAuthenticatedAsync(membershipId!).ConfigureAwait(false);
                    return Result<uint, NetworkFailure>.Ok(connectId);
                }
            }
            else
            {
                await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            masterKeyHandle.Do(handle => handle.Dispose());
        }

        byte[]? membershipIdBytes = applicationInstanceSettings.Membership?.UniqueIdentifier?.ToByteArray();
        return await EstablishAndSaveSecrecyChannelAsync(connectId, membershipIdBytes).ConfigureAwait(false);
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

    #endregion

    #region Session State Management

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
        catch (InvalidProtocolBufferException)
        {
            networkProvider.ClearConnection(connectId);
            Result<Unit, SecureStorageFailure> deleteSecureStateResult =
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
            if (deleteSecureStateResult.IsErr)
            {
                Log.Warning(
                    "[CLIENT-RESTORE-CLEANUP] Failed to delete corrupted state. ConnectId: {ConnectId}, Error: {Error}",
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

    #endregion

    #region Identity & Master Key Management

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

    #endregion

    #region Device Registration

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

    #endregion

    #region Cleanup & Recovery

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

    #endregion
}
