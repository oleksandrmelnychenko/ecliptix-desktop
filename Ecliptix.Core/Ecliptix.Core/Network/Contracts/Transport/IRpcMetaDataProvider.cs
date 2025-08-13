using System;

namespace Ecliptix.Core.Network.Contracts.Transport;

public interface IRpcMetaDataProvider
{
    Guid AppInstanceId { get; }
    Guid DeviceId { get; }
    string? Culture { get;  }
    void SetAppInfo(Guid appInstanceId, Guid deviceId,string? culture);
    void SetCulture(string? culture);
}
