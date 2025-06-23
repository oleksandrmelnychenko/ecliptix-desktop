using System;

namespace Ecliptix.Core.Network;

public class AppInstanceInfo
{
    public Guid AppInstanceId { get; } = Guid.Parse("9147df66-46bd-4bae-87df-6646bd5bae4c");

    public Guid DeviceId { get; } = Guid.Parse("4c5aaf82-a0c4-40e5-9aaf-82a0c470e53f");

    public byte[] ServerPublicKey { get; set; } = [];
    
    /// <summary>
    ///     Server registration.
    /// </summary>
    public Guid? SystemDeviceIdentifier { get; set; }
}