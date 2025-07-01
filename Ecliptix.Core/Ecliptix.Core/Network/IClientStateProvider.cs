using System;

namespace Ecliptix.Core.Network;

public interface IClientStateProvider
{
    Guid AppInstanceId { get; }
    Guid DeviceId { get; }

    void SetClientInfo(Guid appInstanceId, Guid deviceId);
    void Clear();
}
