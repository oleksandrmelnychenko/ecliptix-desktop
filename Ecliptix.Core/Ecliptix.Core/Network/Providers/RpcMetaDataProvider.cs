using System;

namespace Ecliptix.Core.Network.Providers;

public sealed class RpcMetaDataProvider : IRpcMetaDataProvider
{
    public required Guid AppInstanceId { get; set; }
    public required Guid DeviceId { get; set; }

    public void SetAppInfo(Guid appInstanceId, Guid deviceId)
    {
        AppInstanceId = appInstanceId;
        DeviceId = deviceId;
    }

    public void Clear()
    {
        AppInstanceId = Guid.Empty;
        DeviceId = Guid.Empty;
    }
}