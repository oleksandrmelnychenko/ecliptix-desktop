using System;

namespace Ecliptix.Core.Network.Providers;

public interface IRpcMetaDataProvider
{
    Guid AppInstanceId { get; }
    Guid DeviceId { get; }
    string Culture { get; set; }
    void SetAppInfo(Guid appInstanceId, Guid deviceId,string culture);
    void Clear();
}
