using System;

namespace Ecliptix.Core.Network.Providers;

public sealed class RpcMetaDataProvider : IRpcMetaDataProvider
{
    public Guid AppInstanceId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string Culture { get; private set; }

    public void SetAppInfo(Guid appInstanceId, Guid deviceId, string culture)
    {
        AppInstanceId = appInstanceId;
        DeviceId = deviceId;
        Culture = culture;
    }
    
    public void SetCulture(string culture)
    {
        Culture = culture;
    }

    public void Clear()
    {
        AppInstanceId = Guid.Empty;
        DeviceId = Guid.Empty;
        Culture = string.Empty;
    }
}