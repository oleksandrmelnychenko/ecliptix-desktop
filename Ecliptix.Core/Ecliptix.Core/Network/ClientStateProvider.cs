using System;

namespace Ecliptix.Core.Network;

public sealed class ClientStateProvider : IClientStateProvider
{
    public required Guid AppInstanceId { get; set; }
    public required Guid DeviceId { get; set; }

    public void SetClientInfo(Guid appInstanceId, Guid deviceId)
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