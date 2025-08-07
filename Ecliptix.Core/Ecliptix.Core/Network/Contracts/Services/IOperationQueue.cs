using System;
using System.Collections.Generic;
using Ecliptix.Core.Network.Advanced;
using Ecliptix.Core.Network.Services.Queue;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IOperationQueue : IDisposable
{
    Result<string, NetworkFailure> EnqueueOperation(QueuedOperation operation);
    void ClearConnectionQueue(uint connectId);
    IEnumerable<QueuedOperation> GetPendingOperations(uint? connectId = null);
}