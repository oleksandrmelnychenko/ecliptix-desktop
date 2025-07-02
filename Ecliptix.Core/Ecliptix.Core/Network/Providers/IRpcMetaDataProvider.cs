using System;

namespace Ecliptix.Core.Network.Providers;

public interface IRpcMetaDataProvider
{
    Guid AppInstanceId { get; }
    Guid DeviceId { get; }

    void SetAppInfo(Guid appInstanceId, Guid deviceId);
    void Clear();
}
