using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Authentication;
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
    private const int IpGeolocationTimeoutSeconds = 10;

    public async Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {

        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture).ConfigureAwait(false);

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

        Log.Information("[CLIENT-REGISTER] Calling RegisterDevice. ConnectId: {ConnectId}",
            connectId);

        Result<Unit, NetworkFailure> registrationResult = await RegisterDeviceAsync(connectId, settings).ConfigureAwait(false);
        if (registrationResult.IsErr)
        {
            Log.Error("[CLIENT-REGISTER] RegisterDevice failed. ConnectId: {ConnectId}, Error: {Error}",
                connectId, registrationResult.UnwrapErr().Message);
            return false;
        }

        Log.Information("[CLIENT-REGISTER] RegisterDevice completed successfully. ConnectId: {ConnectId}",
            connectId);

        return true;
    }

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
                return Result<uint, NetworkFailure>.Err(restoreResult.UnwrapErr());

            if (restoreResult.Unwrap())
            {
                if (!string.IsNullOrEmpty(membershipId) && await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false))
                {
                    await stateManager.TransitionToAuthenticatedAsync(membershipId).ConfigureAwait(false);
                    Log.Information("[CLIENT-RESTORE] Session restored successfully. ConnectId: {ConnectId}, IsMembershipConfirmed: {Confirmed}, MembershipId: {MembershipId}",
                        connectId, true, membershipId);
                }
                else
                {
                    Log.Information("[CLIENT-RESTORE] Session restored successfully. ConnectId: {ConnectId}, IsMembershipConfirmed: {Confirmed}, Auth: Anonymous",
                        connectId, false);
                }

                return Result<uint, NetworkFailure>.Ok(connectId);
            }
        }

        bool shouldUseAuthenticatedProtocol = false;
        SodiumSecureMemoryHandle? masterKeyHandle = null;

        if (!string.IsNullOrEmpty(membershipId))
        {
            Log.Information("[CLIENT-INIT-CHECK] Checking prerequisites for authenticated protocol. MembershipId: {MembershipId}",
                membershipId);

            bool hasStoredIdentity = await identityService.HasStoredIdentityAsync(membershipId).ConfigureAwait(false);
            Log.Information("[CLIENT-INIT-CHECK] HasStoredIdentity: {HasStoredIdentity}", hasStoredIdentity);

            if (hasStoredIdentity)
            {
                masterKeyHandle = await TryReconstructMasterKeyAsync(membershipId, applicationInstanceSettings).ConfigureAwait(false);

                if (masterKeyHandle != null)
                {
                    Log.Information("[CLIENT-INIT-CHECK] All prerequisites met for authenticated protocol. MembershipId: {MembershipId}",
                        membershipId);
                    shouldUseAuthenticatedProtocol = true;
                }
                else
                {
                    Log.Warning("[CLIENT-INIT-CHECK] Failed to reconstruct master key. Will use anonymous protocol. MembershipId: {MembershipId}",
                        membershipId);
                }
            }
            else
            {
                Log.Information("[CLIENT-INIT-CHECK] No stored identity found. Will use anonymous protocol. MembershipId: {MembershipId}",
                    membershipId);
            }
        }
        else
        {
            Log.Information("[CLIENT-INIT-CHECK] No membership identifier. Will use anonymous protocol.");
        }

        try
        {
            if (shouldUseAuthenticatedProtocol && masterKeyHandle != null)
            {
                ByteString membershipByteString = applicationInstanceSettings.Membership!.UniqueIdentifier;
                Log.Information("[CLIENT-AUTH-HANDSHAKE] Creating authenticated protocol. ConnectId: {ConnectId}, MembershipId: {MembershipId}",
                    connectId, membershipId);

                Result<Unit, NetworkFailure> recreateResult =
                    await networkProvider.RecreateProtocolWithMasterKeyAsync(
                        masterKeyHandle, membershipByteString, connectId).ConfigureAwait(false);

                if (recreateResult.IsErr)
                {
                    NetworkFailure failure = recreateResult.UnwrapErr();

                    if (failure.FailureType == NetworkFailureType.CriticalAuthenticationFailure)
                    {
                        Log.Error(
                            "[CLIENT-AUTH-CRITICAL] Critical authentication failure detected. Server cannot derive identity keys. Performing full cleanup. MembershipId: {MembershipId}, Error: {Error}",
                            membershipId, failure.Message);

                        await CleanupCorruptedIdentityDataAsync(membershipId!, applicationInstanceSettings).ConfigureAwait(false);
                        await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId).ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warning("[CLIENT-AUTH-HANDSHAKE] Authenticated protocol creation failed. Falling back to anonymous. ConnectId: {ConnectId}, Error: {Error}",
                            connectId, failure.Message);
                        await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId).ConfigureAwait(false);
                    }
                }
                else
                {
                    Log.Information("[CLIENT-AUTH-HANDSHAKE] Authenticated protocol created successfully. ConnectId: {ConnectId}",
                        connectId);
                    await stateManager.TransitionToAuthenticatedAsync(membershipId!).ConfigureAwait(false);

                    return Result<uint, NetworkFailure>.Ok(connectId);
                }
            }
            else
            {
                Log.Information("[CLIENT-ANON-HANDSHAKE] Creating anonymous protocol. ConnectId: {ConnectId}",
                    connectId);
                await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId).ConfigureAwait(false);
            }
        }
        finally
        {
            masterKeyHandle?.Dispose();
        }

        byte[]? membershipIdBytes = applicationInstanceSettings.Membership?.UniqueIdentifier?.ToByteArray();
        return await EstablishAndSaveSecrecyChannelAsync(connectId, membershipIdBytes).ConfigureAwait(false);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishAndSaveSecrecyChannelAsync(uint connectId, byte[]? membershipId)
    {
        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        if (membershipId != null)
        {
            Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                secrecyChannelState.ToByteString(),
                span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(), membershipId))
                .ConfigureAwait(false);

            if (saveResult.IsErr)
            {
                Log.Warning("[CLIENT-STATE-SAVE] Failed to save secrecy channel state. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, saveResult.UnwrapErr().Message);
            }
        }
        else
        {
            Log.Warning("[CLIENT-STATE-SAVE] Cannot save state: membershipId not available. ConnectId: {ConnectId}",
                connectId);
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

    private async Task<SodiumSecureMemoryHandle?> TryLoadMasterKeyFromStorageAsync(string membershipId)
    {
        Log.Information("[CLIENT-MASTERKEY-STORAGE] Attempting to load master key from storage. MembershipId: {MembershipId}", membershipId);

        Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult =
            await identityService.LoadMasterKeyHandleAsync(membershipId).ConfigureAwait(false);

        if (loadResult.IsErr)
        {
            Log.Warning("[CLIENT-MASTERKEY-STORAGE] Master key load failed. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, loadResult.UnwrapErr().Message);
            return null;
        }

        SodiumSecureMemoryHandle loadedHandle = loadResult.Unwrap();

        Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult = loadedHandle.ReadBytes(loadedHandle.Length);
        if (readResult.IsOk)
        {
            byte[] masterKeyBytes = readResult.Unwrap();
            string masterKeyFingerprint = Convert.ToHexString(SHA256.HashData(masterKeyBytes))[..16];
            Log.Information("[CLIENT-MASTERKEY-STORAGE-LOADED] Master key loaded from storage with fingerprint. MembershipId: {MembershipId}, Fingerprint: {Fingerprint}",
                membershipId, masterKeyFingerprint);
            CryptographicOperations.ZeroMemory(masterKeyBytes);
        }

        Log.Information("[CLIENT-MASTERKEY-STORAGE] Master key loaded successfully from storage. MembershipId: {MembershipId}", membershipId);
        return loadedHandle;
    }

    private async Task<SodiumSecureMemoryHandle?> TryReconstructMasterKeyAsync(
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        Log.Information("[CLIENT-MASTERKEY] Starting master key load from storage. MembershipId: {MembershipId}", membershipId);

        SodiumSecureMemoryHandle? storageHandle = await TryLoadMasterKeyFromStorageAsync(membershipId).ConfigureAwait(false);

        if (storageHandle != null)
        {
            Log.Information("[CLIENT-MASTERKEY] Master key loaded successfully from storage. MembershipId: {MembershipId}", membershipId);
            return storageHandle;
        }

        Log.Error("[CLIENT-MASTERKEY-RECOVERY] Master key load failed. Triggering automatic data cleanup. MembershipId: {MembershipId}", membershipId);

        await CleanupCorruptedIdentityDataAsync(membershipId, applicationInstanceSettings).ConfigureAwait(false);

        return null;
    }

    private async Task CleanupCorruptedIdentityDataAsync(
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        Log.Warning("[CLIENT-RECOVERY] Starting automatic recovery from corrupted identity data. MembershipId: {MembershipId}", membershipId);

        try
        {
            uint connectId = NetworkProvider.ComputeUniqueConnectId(
                applicationInstanceSettings,
                PubKeyExchangeType.DataCenterEphemeralConnect);

            Result<Unit, Exception> cleanupResult =
                await stateCleanupService.CleanupUserStateWithKeysAsync(membershipId, connectId).ConfigureAwait(false);

            if (cleanupResult.IsErr)
            {
                Log.Error(cleanupResult.UnwrapErr(), "[CLIENT-RECOVERY] Cleanup failed during automatic recovery. MembershipId: {MembershipId}", membershipId);
                return;
            }

            await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

            Log.Information("[CLIENT-RECOVERY] Automatic recovery completed successfully. All corrupted data cleaned. MembershipId: {MembershipId}", membershipId);
            Log.Information("[CLIENT-RECOVERY] User will be redirected to authentication window for fresh login.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-RECOVERY] Failed to cleanup corrupted identity data. MembershipId: {MembershipId}", membershipId);
        }
    }

    private async Task<Result<bool, NetworkFailure>> TryRestoreSessionStateAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        byte[]? membershipId = applicationInstanceSettings.Membership?.UniqueIdentifier?.ToByteArray();
        if (membershipId == null)
        {
            Log.Warning("[CLIENT-RESTORE] Cannot restore: membershipId not available. ConnectId: {ConnectId}",
                connectId);
            return Result<bool, NetworkFailure>.Ok(false);
        }

        Result<byte[], SecureStorageFailure> loadResult =
            await secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);

        if (loadResult.IsErr)
            return Result<bool, NetworkFailure>.Ok(false);

        byte[] stateBytes = loadResult.Unwrap();

        EcliptixSessionState? state;
        try
        {
            state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
        }
        catch (InvalidProtocolBufferException ex)
        {
            Log.Warning("[CLIENT-RESTORE] Failed to parse session state. ConnectId: {ConnectId}, Error: {Error}",
                connectId, ex.Message);
            networkProvider.ClearConnection(connectId);
            var deleteSecureStateResult =
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
            if (deleteSecureStateResult.IsErr)
            {
                Log.Warning("[CLIENT-RESTORE-CLEANUP] Failed to delete corrupted state. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, deleteSecureStateResult.UnwrapErr().Message);
            }
            return Result<bool, NetworkFailure>.Ok(false);
        }

        Result<bool, NetworkFailure> restoreResult =
            await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings).ConfigureAwait(false);

        if (restoreResult.IsErr)
        {
            networkProvider.ClearConnection(connectId);
            Result<Unit, SecureStorageFailure> deleteStateResult =
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
            if (deleteStateResult.IsErr)
            {
                Log.Warning("[CLIENT-RESTORE-CLEANUP] Failed to delete state after restore failure. ConnectId: {ConnectId}, Error: {Error}",
                    connectId, deleteStateResult.UnwrapErr().Message);
            }
            return Result<bool, NetworkFailure>.Ok(false);
        }

        if (restoreResult.Unwrap())
            return Result<bool, NetworkFailure>.Ok(true);

        networkProvider.ClearConnection(connectId);
        Result<Unit, SecureStorageFailure> deleteResult =
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
        if (deleteResult.IsErr)
        {
            Log.Warning("[CLIENT-RESTORE-CLEANUP] Failed to delete state after unsuccessful restore. ConnectId: {ConnectId}, Error: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }

        return Result<bool, NetworkFailure>.Ok(false);
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

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None).ConfigureAwait(false);
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

}
