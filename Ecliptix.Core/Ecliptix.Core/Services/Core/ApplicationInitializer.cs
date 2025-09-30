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
    IDistributedShareStorage? multiLocationStorage = null,
    ISecretSharingService? keySplitter = null,
    IHmacKeyManager? shareAuthenticationService = null
    )
    : IApplicationInitializer
{
    private const int IpGeolocationTimeoutSeconds = 10;
    private const int MinimumSharesForReconstruction = 3;

    public bool IsMembershipConfirmed { get; } = false;

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
            _ = Task.Run(async () =>
            {
                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(IpGeolocationTimeoutSeconds));
                Result<IpCountry, InternalServiceApiFailure> countryResult =
                    await ipGeolocationService.GetIpCountryAsync(cts.Token);

                if (countryResult.IsOk)
                {
                    networkProvider.SetCountry(countryResult.Unwrap().Country);
                    await applicationSecureStorageProvider.SetApplicationIpCountryAsync(countryResult.Unwrap());
                }
            });
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

        if (!isNewInstance)
        {
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            if (!string.IsNullOrEmpty(membershipId))
            {
                await identityService.ClearAllCacheAsync(membershipId);

                if (multiLocationStorage != null)
                {
                    await multiLocationStorage.ClearAllCacheAsync();
                }

                if (shareAuthenticationService != null)
                {
                    await shareAuthenticationService.RemoveHmacKeyAsync(membershipId);
                }
            }

            Result<byte[], SecureStorageFailure> loadResult =
                await secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

            if (loadResult.IsOk)
            {
                byte[] stateBytes = loadResult.Unwrap();
                EcliptixSessionState? state = EcliptixSessionState.Parser.ParseFrom(stateBytes);

                Result<bool, NetworkFailure> restoreSecrecyChannelResult =
                    await networkProvider.RestoreSecrecyChannelAsync(state, applicationInstanceSettings);

                if (restoreSecrecyChannelResult.IsErr)
                    return Result<uint, NetworkFailure>.Err(restoreSecrecyChannelResult.UnwrapErr());

                if (restoreSecrecyChannelResult.IsOk && restoreSecrecyChannelResult.Unwrap())
                {
                    return Result<uint, NetworkFailure>.Ok(connectId);
                }

                networkProvider.ClearConnection(connectId);
                await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
            }
        }

        if (!string.IsNullOrEmpty(membershipId) && await identityService.HasStoredIdentityAsync(membershipId))
        {
            SodiumSecureMemoryHandle? masterKeyHandle = null;

            if (multiLocationStorage != null && keySplitter != null)
            {
                Guid membershipGuid = Guid.Parse(membershipId);
                try
                {
                    Result<bool, KeySplittingFailure> hasShares = await multiLocationStorage.HasStoredSharesAsync(membershipGuid);
                    if (hasShares.IsOk && hasShares.Unwrap())
                    {
                        Result<KeyShare[], KeySplittingFailure> sharesResult = await multiLocationStorage.RetrieveKeySharesAsync(membershipGuid, MinimumSharesForReconstruction);
                        if (sharesResult.IsOk)
                        {
                            KeyShare[] shares = sharesResult.Unwrap();
                            try
                            {
                                SodiumSecureMemoryHandle? hmacKeyHandle = null;
                                if (shareAuthenticationService != null)
                                {
                                    Result<SodiumSecureMemoryHandle, KeySplittingFailure> hmacKeyHandleResult =
                                        await shareAuthenticationService.RetrieveHmacKeyHandleAsync(membershipId);
                                    if (hmacKeyHandleResult.IsOk)
                                    {
                                        hmacKeyHandle = hmacKeyHandleResult.Unwrap();
                                    }
                                    else
                                    {
                                        await keySplitter.SecurelyDisposeSharesAsync(shares);
                                        await multiLocationStorage.RemoveKeySharesAsync(membershipGuid);
                                    }
                                }

                                Result<SodiumSecureMemoryHandle, KeySplittingFailure> reconstructHandleResult =
                                    await keySplitter.ReconstructKeyHandleAsync(shares, hmacKeyHandle);

                                hmacKeyHandle?.Dispose();

                                if (reconstructHandleResult.IsOk)
                                {
                                    masterKeyHandle = reconstructHandleResult.Unwrap();
                                }
                                
                            }
                            finally
                            {
                                foreach (KeyShare share in shares)
                                {
                                    share?.Dispose();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                     await multiLocationStorage.RemoveKeySharesAsync(membershipGuid);
                }
            }

            if (masterKeyHandle == null)
            {
                Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult = await identityService.LoadMasterKeyHandleAsync(membershipId);
                if (loadResult.IsOk)
                {
                    masterKeyHandle = loadResult.Unwrap();
                }
            }

            if (masterKeyHandle != null)
            {
                try
                {
                    Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
                        masterKeyHandle.ReadBytes(masterKeyHandle.Length);

                    if (readResult.IsOk)
                    {
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
                    else
                    {
                        networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
                    }
                }
                finally
                {
                    masterKeyHandle.Dispose();
                }
            }
            else
            {
                networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
            }
        }
        else
        {
            networkProvider.InitiateEcliptixProtocolSystem(applicationInstanceSettings, connectId);
        }

        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await networkProvider.EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        await SecureByteStringInterop.WithByteStringAsSpan(
            secrecyChannelState.ToByteString(),
            span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

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

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None);
    }
}