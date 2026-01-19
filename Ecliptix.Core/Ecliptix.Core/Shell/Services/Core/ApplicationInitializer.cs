using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Membership;
using Ecliptix.Network.Infrastructure.Data;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Core;
using Ecliptix.Network.Services.External.IpGeolocation;
using Ecliptix.Network.Services.Network;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.SecureProtocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Shell.Services.Core;

public sealed class ApplicationInitializer(
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    ILocalizationService localizationService,
    IIpGeolocationService ipGeolocationService,
    IIdentityService identityService,
    IApplicationStateManager stateManager) : IApplicationInitializer
{
    private const int IP_GEOLOCATION_TIMEOUT_SECONDS = 10;

    private readonly PendingLogoutProcessor _pendingLogoutProcessor = new(
        networkProvider,
        new PendingLogoutRequestStorage(applicationSecureStorageProvider));

    public async Task<ApplicationInitializationResult> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {
        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture)
                .ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return ApplicationInitializationResult.SETTINGS_INITIALIZATION_FAILED;
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
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception,
                            "[APPLICATION-INITIALIZER] Unhandled exception fetching IP geolocation");
                    }
                },
                TaskScheduler.Default);
        }

        Result<uint, NetworkFailure> connectIdResult =
            await EnsureSecrecyChannelAsync(settings, isNewInstance).ConfigureAwait(false);
        if (connectIdResult.IsErr)
        {
            NetworkFailure failure = connectIdResult.UnwrapErr();
            Exception? innerException = failure.InnerException;
            if (innerException != null)
            {
                Log.Error(innerException,
                    "[APPLICATION-INITIALIZER] Secrecy channel initialization failed. Type: {FailureType}, Message: {Message}",
                    failure.FailureType,
                    failure.Message);
            }
            else
            {
                Log.Error(
                    "[APPLICATION-INITIALIZER] Secrecy channel initialization failed. Type: {FailureType}, Message: {Message}",
                    failure.FailureType,
                    failure.Message);
            }
            return ApplicationInitializationResult.SECRECY_CHANNEL_FAILED;
        }

        uint connectId = connectIdResult.Unwrap();

        Result<Unit, NetworkFailure> registrationResult =
            await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);
        if (registrationResult.IsErr)
        {
            return ApplicationInitializationResult.DEVICE_REGISTRATION_FAILED;
        }

        await ProcessPendingLogoutRequestsAsync(connectId).ConfigureAwait(false);

        return ApplicationInitializationResult.SUCCESS;
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

        if (!isNewInstance)
        {
            Result<uint, NetworkFailure>? restoreResult =
                await TryRestoreExistingSessionAsync(connectId, applicationInstanceSettings)
                    .ConfigureAwait(false);

            if (restoreResult.HasValue)
            {
                return restoreResult.Value;
            }
        }

        Option<string> membershipId = ExtractMembershipId(applicationInstanceSettings);
        Option<string> accountId = ExtractAccountId(applicationInstanceSettings);

        return await EstablishNewSecrecyChannelAsync(applicationInstanceSettings, connectId, membershipId, accountId)
            .ConfigureAwait(false);
    }

    private static Option<string> ExtractMembershipId(ApplicationInstanceSettings applicationInstanceSettings) =>
        applicationInstanceSettings.Membership?.MembershipId is { IsEmpty: false }
            ? Option<string>.Some(Helpers.FromByteStringToGuid(applicationInstanceSettings.Membership.MembershipId)
                .ToString())
            : Option<string>.None;

    private static Option<string> ExtractAccountId(ApplicationInstanceSettings applicationInstanceSettings) =>
        applicationInstanceSettings.CurrentAccountId is { IsEmpty: false }
            ? Option<string>.Some(Helpers.FromByteStringToGuid(applicationInstanceSettings.CurrentAccountId).ToString())
            : Option<string>.None;

    private async Task<Result<uint, NetworkFailure>?> TryRestoreExistingSessionAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        ClearExistingConnection(connectId);

        Result<bool, NetworkFailure> restoreResult =
            await TryRestoreSessionStateAsync(connectId, applicationInstanceSettings).ConfigureAwait(false);

        if (restoreResult.IsErr)
        {
            NetworkFailure failure = restoreResult.UnwrapErr();
            if (ShouldFallbackFromRestoreFailure(failure))
            {
                await HandleRestoreFallbackAsync(connectId, applicationInstanceSettings, failure)
                    .ConfigureAwait(false);
                return null;
            }

            return Result<uint, NetworkFailure>.Err(failure);
        }

        if (!restoreResult.Unwrap())
        {
            return null;
        }

        Option<string> membershipId = ExtractMembershipId(applicationInstanceSettings);
        Option<string> accountId = ExtractAccountId(applicationInstanceSettings);

        if (membershipId.IsSome && accountId.IsSome)
        {
            string membershipIdValue = membershipId.Value!;
            string accountIdValue = accountId.Value!;
            if (await identityService.HasStoredIdentityAsync(accountIdValue).ConfigureAwait(false))
            {
                await stateManager.TransitionToAuthenticatedAsync(membershipIdValue).ConfigureAwait(false);
            }
        }

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private void ClearExistingConnection(uint connectId)
    {
        if (!networkProvider.HasConnection(connectId))
        {
            return;
        }

        Log.Warning(
            "[APPLICATION-INITIALIZER] Existing protocol session found before restore. Clearing to avoid conflicts. ConnectId: {ConnectId}",
            connectId);
        networkProvider.ClearConnection(connectId);
    }

    private static bool ShouldFallbackFromRestoreFailure(NetworkFailure failure)
    {
        if (FailureClassification.IsProtocolStateMismatch(failure) ||
            FailureClassification.IsSessionExpired(failure))
        {
            return true;
        }

        return failure.FailureType == NetworkFailureType.ECLIPTIX_PROTOCOL_FAILURE &&
               failure.Message.Contains("session expired", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleRestoreFallbackAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings,
        NetworkFailure failure)
    {
        Log.Warning(
            "[APPLICATION-INITIALIZER] Restore failed; resetting local state and falling back to anonymous. Type: {FailureType}, ConnectId: {ConnectId}, Message: {Message}",
            failure.FailureType,
            connectId,
            failure.Message);

        networkProvider.ClearConnection(connectId);

        Result<Unit, SecureStorageFailure> deleteResult =
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
        if (deleteResult.IsErr)
        {
            Log.Warning(
                "[APPLICATION-INITIALIZER] Failed to delete protocol state during fallback. ConnectId: {ConnectId}, Error: {Error}",
                connectId,
                deleteResult.UnwrapErr().Message);
        }

        if (applicationInstanceSettings.Membership != null)
        {
            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);
            applicationInstanceSettings.Membership = null;
        }

        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishNewSecrecyChannelAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId,
        Option<string> membershipId,
        Option<string> accountId)
    {
        Option<SodiumSecureMemoryHandle> masterKeyHandle =
            await PrepareMasterKeyHandleAsync(accountId, applicationInstanceSettings)
                .ConfigureAwait(false);

        try
        {
            bool shouldUseAuthenticatedProtocol = masterKeyHandle.IsSome && membershipId.IsSome && accountId.IsSome;

            if (shouldUseAuthenticatedProtocol)
            {
                Result<uint, NetworkFailure>? authenticatedResult =
                    await TryUseAuthenticatedProtocolAsync(
                            applicationInstanceSettings,
                            connectId,
                            membershipId.Value!,
                            accountId.Value!,
                            masterKeyHandle.Value!)
                        .ConfigureAwait(false);

                if (authenticatedResult.HasValue)
                {
                    return authenticatedResult.Value;
                }
            }

            await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
                .ConfigureAwait(false);

            ByteString? accountIdentifier = applicationInstanceSettings.CurrentAccountId;
            Option<byte[]> accountIdBytes = accountIdentifier is { IsEmpty: false }
                ? Option<byte[]>.Some(accountIdentifier.ToByteArray())
                : Option<byte[]>.None;

            return await EstablishAndPersistSecrecyChannelAsync(connectId, accountIdBytes).ConfigureAwait(false);
        }
        finally
        {
            masterKeyHandle.Do(handle => handle.Dispose());
        }
    }

    private async Task<Option<SodiumSecureMemoryHandle>> PrepareMasterKeyHandleAsync(
        Option<string> accountId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (!accountId.IsSome)
        {
            return Option<SodiumSecureMemoryHandle>.None;
        }

        string accountIdValue = accountId.Value!;
        bool hasStoredIdentity = await identityService.HasStoredIdentityAsync(accountIdValue).ConfigureAwait(false);
        if (!hasStoredIdentity)
        {
            return Option<SodiumSecureMemoryHandle>.None;
        }

        return await TryReconstructMasterKeyAsync(accountIdValue, applicationInstanceSettings)
            .ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>?> TryUseAuthenticatedProtocolAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId,
        string membershipId,
        string accountId,
        SodiumSecureMemoryHandle masterKeyHandle)
    {
        if (applicationInstanceSettings.Membership?.MembershipId == null)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(
                    "Membership information is missing for authenticated protocol"));
        }

        if (applicationInstanceSettings.CurrentAccountId == null ||
            applicationInstanceSettings.CurrentAccountId.IsEmpty)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(
                    "Account information is missing for authenticated protocol"));
        }

        ByteString membershipByteString = applicationInstanceSettings.Membership.MembershipId;
        ByteString accountByteString = applicationInstanceSettings.CurrentAccountId;

        Result<Unit, NetworkFailure> recreateResult =
            await networkProvider.RecreateProtocolWithMasterKeyAsync(
                masterKeyHandle, membershipByteString, accountByteString, connectId).ConfigureAwait(false);

        if (recreateResult.IsErr)
        {
            await HandleAuthenticatedProtocolFailureAsync(recreateResult.UnwrapErr(), accountId,
                    applicationInstanceSettings, connectId)
                .ConfigureAwait(false);
            return null;
        }

        await stateManager.TransitionToAuthenticatedAsync(membershipId).ConfigureAwait(false);
        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task HandleAuthenticatedProtocolFailureAsync(
        NetworkFailure failure,
        string accountId,
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId)
    {
        if (failure.FailureType == NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE)
        {
            await CleanupCorruptedIdentityDataAsync(accountId, applicationInstanceSettings)
                .ConfigureAwait(false);
        }

        await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId)
            .ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishAndPersistSecrecyChannelAsync(uint connectId,
        Option<byte[]> accountId)
    {
        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        if (accountId.IsSome)
        {
            await SecureByteStringInterop.WithByteStringAsSpan(
                    secrecyChannelState.ToByteString(),
                    span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(),
                        accountId.Value!))
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
        ByteString? accountId = applicationInstanceSettings.CurrentAccountId;
        if (accountId == null || accountId.IsEmpty)
        {
            return Result<bool, NetworkFailure>.Ok(false);
        }

        Result<byte[], SecureStorageFailure> loadResult =
            await secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), accountId.ToByteArray()).ConfigureAwait(false);

        if (loadResult.IsErr)
        {
            return Result<bool, NetworkFailure>.Ok(false);
        }

        byte[] stateBytes = loadResult.Unwrap();

        Option<EcliptixSessionState> state;
        try
        {
            state = Option<EcliptixSessionState>.Some(EcliptixSessionState.Parser.ParseFrom(stateBytes));
        }
        catch (InvalidProtocolBufferException ex)
        {
            networkProvider.ClearConnection(connectId);
            Result<Unit, SecureStorageFailure> deleteSecureStateResult =
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
            if (deleteSecureStateResult.IsErr)
            {
                Log.Warning(ex,
                    "[CLIENT-RESTORE-CLEANUP] Failed to delete corrupted state. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, deleteSecureStateResult.UnwrapErr().Message);
            }

            return Result<bool, NetworkFailure>.Ok(false);
        }

        if (!state.IsSome)
        {
            return Result<bool, NetworkFailure>.Ok(false);
        }

        string membershipIdString = SecureByteStringInterop.WithByteStringAsSpan(
            applicationInstanceSettings.Membership!.MembershipId!,
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
            await networkProvider.RestoreSecrecyChannelAsync(state.Value!, applicationInstanceSettings).ConfigureAwait(false);

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

    private async Task<Option<SodiumSecureMemoryHandle>> TryLoadMasterKeyFromStorageAsync(string accountId)
    {
        Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult =
            await identityService.LoadMasterKeyHandleAsync(accountId).ConfigureAwait(false);

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
        string accountId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        Option<SodiumSecureMemoryHandle> storageHandle =
            await TryLoadMasterKeyFromStorageAsync(accountId).ConfigureAwait(false);

        if (storageHandle.IsSome)
        {
            return storageHandle;
        }

        await CleanupCorruptedIdentityDataAsync(accountId, applicationInstanceSettings).ConfigureAwait(false);

        return Option<SodiumSecureMemoryHandle>.None;
    }

    private async Task<Result<Unit, NetworkFailure>> RegisterDeviceAsync(uint connectId,
        ApplicationInstanceSettings settings)
    {
        Device device = new()
        {
            ApplicationInstanceId = settings.AppInstanceId,
            DeviceId = settings.DeviceId,
            DeviceType = DeviceType.Desktop
        };

        return await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegisterAppDevice,
            SecureByteStringInterop.WithByteStringAsSpan(device.ToByteString(),
                span => span.ToArray()),
            decryptedPayload =>
            {
                DeviceRegistrationResponse reply =
                    Helpers.ParseFromBytes<DeviceRegistrationResponse>(decryptedPayload);

                if (reply.Result is DeviceRegistrationResponse.Types.Result.DeviceRegistrationResultInvalidRequest
                    or DeviceRegistrationResponse.Types.Result.DeviceRegistrationResultInternalError)
                {
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType(
                            string.IsNullOrWhiteSpace(reply.Message)
                                ? "Device registration failed"
                                : reply.Message)));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: false, token: CancellationToken.None).ConfigureAwait(false);
    }

    private async Task CleanupCorruptedIdentityDataAsync(
        string accountId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            applicationInstanceSettings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Unit, Exception> cleanupResult =
            await identityService.CleanupMembershipStateWithKeysAsync(accountId, connectId)
                .ConfigureAwait(false);

        networkProvider.ClearConnection(connectId);

        if (cleanupResult.IsErr)
        {
            return;
        }

        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);
    }
}
