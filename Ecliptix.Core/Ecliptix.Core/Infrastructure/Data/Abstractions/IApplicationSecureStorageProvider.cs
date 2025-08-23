using System;
using System.Threading.Tasks;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Data.Abstractions;

public interface IApplicationSecureStorageProvider : IAsyncDisposable
{
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationSettingsCultureAsync(string? cultureName);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationInstanceAsync(bool isNewInstance);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationIpCountryAsync(IpCountry ipCountry);
    Task<Result<Unit, InternalServiceApiFailure>> SetApplicationMembershipAsync(Membership membership);
    Task<Result<ApplicationInstanceSettings, InternalServiceApiFailure>> GetApplicationInstanceSettingsAsync();

    Task<Result<InstanceSettingsResult, InternalServiceApiFailure>> InitApplicationInstanceSettingsAsync(
        string? defaultCulture);

    Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data);
    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);
    Result<Unit, InternalServiceApiFailure> DeleteAsync(string key);
}