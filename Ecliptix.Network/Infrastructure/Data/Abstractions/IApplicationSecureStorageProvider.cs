using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Core;
using Ecliptix.Network.Services.External.IpGeolocation;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Network.Infrastructure.Data.Abstractions;

public interface IApplicationSecureStorageProvider : IAsyncDisposable
{
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string? cultureName);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationInstanceAsync(bool isNewInstance);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationIpCountryAsync(IpCountry ipCountry);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationMembershipAsync(ByteString? membershipId);
    Task<Result<Unit, InternalServiceApiFailure>> SetCurrentAccountIdAsync(ByteString? accountId);

    Task<Result<Unit, InternalServiceApiFailure>> SetServerPublicKeyAsync(
        PubKeyExchangeType exchangeType, ByteString? serverPublicKey);

    Task<Result<Unit, InternalServiceApiFailure>> SetServerKyberPublicKeyAsync(
        PubKeyExchangeType exchangeType, ByteString? serverKyberPublicKey);

    Task<Result<Option<ByteString>, InternalServiceApiFailure>> GetServerPublicKeyAsync(
        PubKeyExchangeType exchangeType);

    Task<Result<Option<ByteString>, InternalServiceApiFailure>> GetServerKyberPublicKeyAsync(
        PubKeyExchangeType exchangeType);

    Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>> GetApplicationInstanceSettingsAsync();

    Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string? defaultCulture);
    Task<Result<Unit, InternalServiceApiFailure>> SetWindowPlacementAsync(WindowPlacement windowPlacement);

    Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data);
    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);
    Result<Unit, InternalServiceApiFailure> Delete(string key);
}
