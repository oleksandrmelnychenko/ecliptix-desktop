using System;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface ISingleInstanceManager : IDisposable
{
    bool TryAcquireInstance();

    bool NotifyExistingInstance();

    event EventHandler? InstanceActivationRequested;
}