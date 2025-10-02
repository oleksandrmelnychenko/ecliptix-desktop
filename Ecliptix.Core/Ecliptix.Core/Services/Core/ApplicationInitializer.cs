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
    IHmacKeyManager hmacKeyManager): IApplicationInitializer
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

        Result<Unit, NetworkFailure> registrationResult = await RegisterDeviceAsync(connectId, settings);
        if (registrationResult.IsErr)
        {
            return false;
        }

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
                return Result<uint, NetworkFailure>.Ok(connectId);
        }

        if (!string.IsNullOrEmpty(membershipId) && await identityService.HasStoredIdentityAsync(membershipId))
        {
            SodiumSecureMemoryHandle? masterKeyHandle = await TryReconstructMasterKeyAsync(membershipId);

            if (masterKeyHandle != null)
            {
                InitializeProtocolWithIdentity(
                    masterKeyHandle, membershipId, applicationInstanceSettings, connectId);
            }
            else
            {
                await InitializeProtocolWithoutIdentityAsync(applicationInstanceSettings, connectId);
            }
        }
        else
        {
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

    private void InitializeProtocolWithIdentity(
        SodiumSecureMemoryHandle masterKeyHandle,
        string membershipId,
        ApplicationInstanceSettings applicationInstanceSettings,
        uint connectId)
    {
        IsMembershipConfirmed = true;

        try
        {
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
                masterKeyHandle.ReadBytes(masterKeyHandle.Length);

            if (readResult.IsErr)
            {
                networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
                return;
            }

            byte[] masterKeyBytes = readResult.Unwrap();

            try
            {
                Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> identityResult =
                    EcliptixSystemIdentityKeys.CreateFromMasterKey(
                        masterKeyBytes, membershipId, NetworkProvider.DefaultOneTimeKeyCount);

                if (identityResult.IsOk)
                {
                    networkProvider.InitiateEcliptixProtocolSystemWithIdentity(
                        applicationInstanceSettings, connectId, identityResult.Unwrap());
                }
                else
                {
                    networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }
        }
        finally
        {
            masterKeyHandle.Dispose();
        }
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

        try
        {
            Result<bool, KeySplittingFailure> hasShares =
                await distributedShareStorage.HasStoredSharesAsync(membershipGuid);

            if (hasShares.IsErr || !hasShares.Unwrap())
                return null;

            Result<KeyShare[], KeySplittingFailure> sharesResult =
                await distributedShareStorage.RetrieveKeySharesAsync(membershipGuid);

            if (sharesResult.IsErr)
                return null;

            KeyShare[] shares = sharesResult.Unwrap();
            try
            {
                SodiumSecureMemoryHandle? hmacKeyHandle =
                    await TryRetrieveHmacKeyHandleAsync(membershipId);

                if (hmacKeyHandle == null)
                {
                    await secretSharingService.SecurelyDisposeSharesAsync(shares);
                    await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
                    return null;
                }

                Result<SodiumSecureMemoryHandle, KeySplittingFailure> reconstructResult =
                    await secretSharingService.ReconstructKeyHandleAsync(shares, hmacKeyHandle);

                hmacKeyHandle.Dispose();

                return reconstructResult.IsOk ? reconstructResult.Unwrap() : null;
            }
            finally
            {
                foreach (KeyShare share in shares)
                {
                    share.Dispose();
                }
            }
        }
        catch
        {
            await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
            return null;
        }
    }

    private async Task<SodiumSecureMemoryHandle?> TryRetrieveHmacKeyHandleAsync(string membershipId)
    {
        Result<SodiumSecureMemoryHandle, KeySplittingFailure> hmacKeyResult =
            await hmacKeyManager.RetrieveHmacKeyHandleAsync(membershipId);

        return hmacKeyResult.IsOk ? hmacKeyResult.Unwrap() : null;
    }

    private async Task<SodiumSecureMemoryHandle?> TryLoadMasterKeyFromStorageAsync(string membershipId)
    {
        Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult =
            await identityService.LoadMasterKeyHandleAsync(membershipId);

        return loadResult.IsOk ? loadResult.Unwrap() : null;
    }

    private async Task<SodiumSecureMemoryHandle?> TryReconstructMasterKeyAsync(string membershipId)
    {
        SodiumSecureMemoryHandle? masterKeyHandle =
            await TryReconstructMasterKeyFromDistributedSharesAsync(membershipId);

        if (masterKeyHandle != null)
            return masterKeyHandle;

        return await TryLoadMasterKeyFromStorageAsync(membershipId);
    }

    private async Task<Result<bool, NetworkFailure>> TryRestoreSessionStateAsync(
        uint connectId,
        ApplicationInstanceSettings applicationInstanceSettings)
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
            return Result<bool, NetworkFailure>.Err(restoreResult.UnwrapErr());

        if (restoreResult.Unwrap())
            return Result<bool, NetworkFailure>.Ok(true);

        networkProvider.ClearConnection(connectId);
        await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

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