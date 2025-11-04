using System;

namespace Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;

public interface IRpcMetaDataProvider
{
    Guid AppInstanceId { get; }
    Guid DeviceId { get; }
    string? CULTURE { get; }
    string LocalIpAddress { get; }
    string? PublicIpAddress { get; }
    string Platform { get; }
    void SetAppInfo(Guid appInstanceId, Guid deviceId, string? culture);
    void SetCulture(string? culture);
}
