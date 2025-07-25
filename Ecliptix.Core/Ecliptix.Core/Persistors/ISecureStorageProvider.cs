using System;
using System.Threading.Tasks;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Persistors;

public interface ISecureStorageProvider : IAsyncDisposable
{
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string cultureName);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationIpCountryAsync(IpCountry ipCountry);
    Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>> GetApplicationInstanceSettingsAsync();

    Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string defaultCulture);

    Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data);
    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);
    Result<Unit, InternalServiceApiFailure> DeleteAsync(string key);
}