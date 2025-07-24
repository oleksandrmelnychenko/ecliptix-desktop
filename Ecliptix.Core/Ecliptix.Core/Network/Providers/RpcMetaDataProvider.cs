using System;

namespace Ecliptix.Core.Network.Providers;

public sealed class RpcMetaDataProvider : IRpcMetaDataProvider
{
    public required Guid AppInstanceId { get; set; }
    public required Guid DeviceId { get; set; }
    public required string Culture { get; set; }

    public void SetAppInfo(Guid appInstanceId, Guid deviceId, string culture)
    {
        AppInstanceId = appInstanceId;
        DeviceId = deviceId;
        Culture = culture;
    }

    public void Clear()
    {
        AppInstanceId = Guid.Empty;
        DeviceId = Guid.Empty;
        Culture = string.Empty;
    }
}