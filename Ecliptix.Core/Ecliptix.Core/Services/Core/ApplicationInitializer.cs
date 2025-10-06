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
using Ecliptix.Core.Infrastructure.Security.KeySplitting;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public record InstanceSettingsResult(ApplicationInstanceSettings Settings, bool IsNewInstance);

public class ApplicationInitializer(
    NetworkProvider networkProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    ILocalizationService localizationService,
    ISystemEventService systemEvents,
    IIpGeolocationService ipGeolocationService,
    IIdentityService identityService,
    IDistributedShareStorage distributedShareStorage,
    ISecretSharingService secretSharingService,
    IHmacKeyManager hmacKeyManager) : IApplicationInitializer
{
    private const int IpGeolocationTimeoutSeconds = 10;

    public bool IsMembershipConfirmed { get; private set; }

    public async Task<bool> InitializeAsync(DefaultSystemSettings defaultSystemSettings)
    {
        await systemEvents.NotifySystemStateAsync(SystemState.Initializing);

        Result<InstanceSettingsResult, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.InitApplicationInstanceSettingsAsync(defaultSystemSettings.Culture);

        if (settingsResult.IsErr)
        {
            await systemEvents.NotifySystemStateAsync(SystemState.FatalError);
            return false;
        }

        (ApplicationInstanceSettings settings, bool isNewInstance) = settingsResult.Unwrap();

        _ = Task.Run(async () =>
        {
            await applicationSecureStorageProvider.SetApplicationInstanceAsync(isNewInstance);
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
            await EnsureSecrecyChannelAsync(settings, isNewInstance);
        if (connectIdResult.IsErr)
        {
            return false;
        }

        uint connectId = connectIdResult.Unwrap();

        Log.Information("[CLIENT-REGISTER] Calling RegisterDevice. ConnectId: {ConnectId}",
            connectId);

        Result<Unit, NetworkFailure> registrationResult = await RegisterDeviceAsync(connectId, settings);
        if (registrationResult.IsErr)
        {
            Log.Error("[CLIENT-REGISTER] RegisterDevice failed. ConnectId: {ConnectId}, Error: {Error}",
                connectId, registrationResult.UnwrapErr().Message);
            return false;
        }

        Log.Information("[CLIENT-REGISTER] RegisterDevice completed successfully. ConnectId: {ConnectId}",
            connectId);

        await systemEvents.NotifySystemStateAsync(SystemState.Running);
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

            //await CleanupForTestingAsync(connectId, membershipId);

        if (!isNewInstance)
        {
            Result<bool, NetworkFailure> restoreResult =
                await TryRestoreSessionStateAsync(connectId, applicationInstanceSettings);

            if (restoreResult.IsErr)
                return Result<uint, NetworkFailure>.Err(restoreResult.UnwrapErr());

            if (restoreResult.Unwrap())
            {
                if (!string.IsNullOrEmpty(membershipId) && await identityService.HasStoredIdentityAsync(membershipId))
                {
                    IsMembershipConfirmed = true;
                    Log.Information("[CLIENT-RESTORE] Session restored successfully. ConnectId: {ConnectId}, IsMembershipConfirmed: {Confirmed}, MembershipId: {MembershipId}",
                        connectId, IsMembershipConfirmed, membershipId);
                }
                else
                {
                    Log.Information("[CLIENT-RESTORE] Session restored successfully. ConnectId: {ConnectId}, IsMembershipConfirmed: {Confirmed}, Auth: Anonymous",
                        connectId, IsMembershipConfirmed);
                }

                return Result<uint, NetworkFailure>.Ok(connectId);
            }
        }

        Log.Information("[CLIENT-INIT-CHECK] Checking for stored identity. MembershipId: {MembershipId}, HasMembershipId: {HasMembershipId}",
            membershipId ?? "NULL", !string.IsNullOrEmpty(membershipId));

        if (!string.IsNullOrEmpty(membershipId))
        {
            bool hasStoredIdentity = await identityService.HasStoredIdentityAsync(membershipId);
            Log.Information("[CLIENT-INIT-CHECK] HasStoredIdentity result: {HasStoredIdentity}, MembershipId: {MembershipId}",
                hasStoredIdentity, membershipId);

            if (hasStoredIdentity)
            {
                SodiumSecureMemoryHandle? masterKeyHandle = await TryReconstructMasterKeyAsync(membershipId);

                if (masterKeyHandle != null)
                {
                    Log.Information("[CLIENT-MASTERKEY] Master key reconstructed successfully. MembershipId: {MembershipId}",
                        membershipId);

                    try
                    {
                        ByteString membershipByteString = applicationInstanceSettings.Membership!.UniqueIdentifier;
                        Log.Information("[CLIENT-AUTH-HANDSHAKE] Creating authenticated protocol. ConnectId: {ConnectId}, MembershipId: {MembershipId}",
                            connectId, membershipId);

                        Result<Unit, NetworkFailure> recreateResult =
                            await networkProvider.RecreateProtocolWithMasterKeyAsync(
                                masterKeyHandle, membershipByteString, connectId);

                        if (recreateResult.IsErr)
                        {
                            Log.Warning("[CLIENT-AUTH-HANDSHAKE] Authenticated protocol creation failed. Falling back to anonymous. ConnectId: {ConnectId}, Error: {Error}",
                                connectId, recreateResult.UnwrapErr().Message);
                            await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId);
                        }
                        else
                        {
                            Log.Information("[CLIENT-AUTH-HANDSHAKE] Authenticated protocol created successfully. ConnectId: {ConnectId}",
                                connectId);
                            IsMembershipConfirmed = true;
                            return Result<uint, NetworkFailure>.Ok(connectId);
                        }
                    }
                    finally
                    {
                        masterKeyHandle.Dispose();
                    }
                }
                else
                {
                    Log.Warning("[CLIENT-MASTERKEY] Failed to reconstruct master key. Falling back to anonymous. MembershipId: {MembershipId}",
                        membershipId);
                    await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId);
                }
            }
            else
            {
                Log.Information("[CLIENT-INIT-CHECK] No stored identity found. Falling back to anonymous. MembershipId: {MembershipId}",
                    membershipId);
                await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId);
            }
        }
        else
        {
            Log.Information("[CLIENT-INIT] Initializing anonymous protocol. ConnectId: {ConnectId}",
                connectId);
            networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
        }

        return await EstablishAndSaveSecrecyChannelAsync(connectId);
    }

    private async Task<Result<uint, NetworkFailure>> EstablishAndSaveSecrecyChannelAsync(uint connectId)
    {
        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        await SecureByteStringInterop.WithByteStringAsSpan(
            secrecyChannelState.ToByteString(),
            span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()))
            .ConfigureAwait(false);

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private async Task InitializeProtocolWithoutIdentityAsync(
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId)
    {
        IsMembershipConfirmed = false;

        if (applicationInstanceSettings.Membership != null)
        {
            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null);
        }

        networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
    }

    private async Task<SodiumSecureMemoryHandle?> TryReconstructMasterKeyFromDistributedSharesAsync(string membershipId)
    {
        Guid membershipGuid = Guid.Parse(membershipId);
        Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] Attempting to reconstruct from distributed shares. MembershipId: {MembershipId}", membershipId);

        try
        {
            Result<bool, KeySplittingFailure> hasShares =
                await distributedShareStorage.HasStoredSharesAsync(membershipGuid);

            if (hasShares.IsErr)
            {
                Log.Warning("[CLIENT-MASTERKEY-DISTRIBUTED] HasStoredSharesAsync failed. MembershipId: {MembershipId}, Error: {Error}",
                    membershipId, hasShares.UnwrapErr().Message);
                return null;
            }

            if (!hasShares.Unwrap())
            {
                Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] No distributed shares found. MembershipId: {MembershipId}", membershipId);
                return null;
            }

            Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] Distributed shares exist. Retrieving... MembershipId: {MembershipId}", membershipId);

            Result<KeyShare[], KeySplittingFailure> sharesResult =
                await distributedShareStorage.RetrieveKeySharesAsync(membershipGuid);

            if (sharesResult.IsErr)
            {
                Log.Warning("[CLIENT-MASTERKEY-DISTRIBUTED] RetrieveKeySharesAsync failed. MembershipId: {MembershipId}, Error: {Error}",
                    membershipId, sharesResult.UnwrapErr().Message);
                return null;
            }

            KeyShare[] shares = sharesResult.Unwrap();
            Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] Retrieved {ShareCount} shares. MembershipId: {MembershipId}",
                shares.Length, membershipId);

            try
            {
                SodiumSecureMemoryHandle? hmacKeyHandle =
                    await TryRetrieveHmacKeyHandleAsync(membershipId);

                if (hmacKeyHandle == null)
                {
                    Log.Warning("[CLIENT-MASTERKEY-DISTRIBUTED] HMAC key not found. Cleaning up shares. MembershipId: {MembershipId}", membershipId);
                    await secretSharingService.SecurelyDisposeSharesAsync(shares);
                    await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
                    return null;
                }

                Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] HMAC key retrieved. Reconstructing master key... MembershipId: {MembershipId}", membershipId);

                Result<SodiumSecureMemoryHandle, KeySplittingFailure> reconstructResult =
                    await secretSharingService.ReconstructKeyHandleAsync(shares, hmacKeyHandle);

                hmacKeyHandle.Dispose();

                if (reconstructResult.IsErr)
                {
                    Log.Warning("[CLIENT-MASTERKEY-DISTRIBUTED] Reconstruction failed. MembershipId: {MembershipId}, Error: {Error}",
                        membershipId, reconstructResult.UnwrapErr().Message);
                    return null;
                }

                Log.Information("[CLIENT-MASTERKEY-DISTRIBUTED] Successfully reconstructed master key from distributed shares. MembershipId: {MembershipId}", membershipId);
                return reconstructResult.Unwrap();
            }
            finally
            {
                foreach (KeyShare share in shares)
                {
                    share.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[CLIENT-MASTERKEY-DISTRIBUTED] Exception during reconstruction. MembershipId: {MembershipId}, Exception: {Exception}",
                membershipId, ex.Message);
            await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
            return null;
        }
    }

    private async Task<SodiumSecureMemoryHandle?> TryRetrieveHmacKeyHandleAsync(string membershipId)
    {
        Log.Information("[CLIENT-MASTERKEY-HMAC] Attempting to retrieve HMAC key. MembershipId: {MembershipId}", membershipId);

        Result<SodiumSecureMemoryHandle, KeySplittingFailure> hmacKeyResult =
            await hmacKeyManager.RetrieveHmacKeyHandleAsync(membershipId);

        if (hmacKeyResult.IsErr)
        {
            Log.Warning("[CLIENT-MASTERKEY-HMAC] HMAC key retrieval failed. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, hmacKeyResult.UnwrapErr().Message);
            return null;
        }

        Log.Information("[CLIENT-MASTERKEY-HMAC] HMAC key retrieved successfully. MembershipId: {MembershipId}", membershipId);
        return hmacKeyResult.Unwrap();
    }

    private async Task<SodiumSecureMemoryHandle?> TryLoadMasterKeyFromStorageAsync(string membershipId)
    {
        Log.Information("[CLIENT-MASTERKEY-STORAGE] Attempting to load master key from storage. MembershipId: {MembershipId}", membershipId);

        Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult =
            await identityService.LoadMasterKeyHandleAsync(membershipId);

        if (loadResult.IsErr)
        {
            Log.Warning("[CLIENT-MASTERKEY-STORAGE] Master key load failed. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, loadResult.UnwrapErr().Message);
            return null;
        }

        Log.Information("[CLIENT-MASTERKEY-STORAGE] Master key loaded successfully from storage. MembershipId: {MembershipId}", membershipId);
        return loadResult.Unwrap();
    }

    private async Task<SodiumSecureMemoryHandle?> TryReconstructMasterKeyAsync(string membershipId)
    {
        Log.Information("[CLIENT-MASTERKEY] Starting master key reconstruction. MembershipId: {MembershipId}", membershipId);

        SodiumSecureMemoryHandle? masterKeyHandle =
            await TryReconstructMasterKeyFromDistributedSharesAsync(membershipId);

        if (masterKeyHandle != null)
        {
            Log.Information("[CLIENT-MASTERKEY] Master key reconstructed from distributed shares. MembershipId: {MembershipId}", membershipId);
            return masterKeyHandle;
        }

        Log.Information("[CLIENT-MASTERKEY] Distributed shares failed, trying direct storage load. MembershipId: {MembershipId}", membershipId);

        SodiumSecureMemoryHandle? storageHandle = await TryLoadMasterKeyFromStorageAsync(membershipId);

        if (storageHandle != null)
        {
            Log.Information("[CLIENT-MASTERKEY] Master key loaded from storage. MembershipId: {MembershipId}", membershipId);
            return storageHandle;
        }

        Log.Warning("[CLIENT-MASTERKEY] All master key reconstruction methods failed. MembershipId: {MembershipId}", membershipId);
        return null;
    }

    private async Task<Result<bool, NetworkFailure>> TryRestoreSessionStateAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        try
        {
            Result<byte[], SecureStorageFailure> loadResult =
                await secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

            if (loadResult.IsErr)
                return Result<bool, NetworkFailure>.Ok(false);

            byte[] stateBytes = loadResult.Unwrap();
            EcliptixSessionState? state = EcliptixSessionState.Parser.ParseFrom(stateBytes);

            Result<bool, NetworkFailure> restoreResult =
                await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings);

            if (restoreResult.IsErr)
            {
                networkProvider.ClearConnection(connectId);
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
                return Result<bool, NetworkFailure>.Ok(false);
            }

            if (restoreResult.Unwrap())
                return Result<bool, NetworkFailure>.Ok(true);

            networkProvider.ClearConnection(connectId);
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            return Result<bool, NetworkFailure>.Ok(false);
        }
        catch (Exception)
        {
            networkProvider.ClearConnection(connectId);
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
            return Result<bool, NetworkFailure>.Ok(false);
        }
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
            }, false, CancellationToken.None);
    }

    private Task FetchIpGeolocationInBackgroundAsync() =>
        Task.Run(async () =>
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(IpGeolocationTimeoutSeconds));
            Result<IpCountry, InternalServiceApiFailure> countryResult =
                await ipGeolocationService.GetIpCountryAsync(cts.Token);

            if (countryResult.IsOk)
            {
                IpCountry country = countryResult.Unwrap();
                networkProvider.SetCountry(country.Country);
                await applicationSecureStorageProvider.SetApplicationIpCountryAsync(country);
            }
        });

    public async Task CleanupForTestingAsync(uint connectId, string? membershipId = null)
    {
        await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

        if (!string.IsNullOrEmpty(membershipId))
        {
            await identityService.ClearAllCacheAsync(membershipId);

            await distributedShareStorage.ClearAllCacheAsync();

            await hmacKeyManager.RemoveHmacKeyAsync(membershipId);

            Guid membershipGuid = Guid.Parse(membershipId);
            await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
        }

        await applicationSecureStorageProvider.SetApplicationMembershipAsync(null);

        networkProvider.ClearConnection(connectId);

        IsMembershipConfirmed = false;
    }
}