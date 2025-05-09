using System;

namespace Ecliptix.Core.Network;

public class AppInstanceInfo
{
    public Guid AppInstanceId { get; } = Guid.NewGuid();

    public Guid DeviceId { get; } = Guid.NewGuid();

    /// <summary>
    ///     Server registration.
    /// </summary>
    public Guid? SystemAppDeviceId { get; set; }
}