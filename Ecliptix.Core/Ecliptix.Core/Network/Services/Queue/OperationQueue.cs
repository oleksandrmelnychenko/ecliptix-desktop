using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Core.Configuration;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Queue;

public enum OperationPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public record QueuedOperation
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public uint ConnectId { get; init; }
    public OperationType Type { get; init; }
    public OperationPriority Priority { get; init; } = OperationPriority.Normal;
    public DateTime QueuedAt { get; init; } = DateTime.UtcNow;
    public DateTime? LastAttemptAt { get; init; }
    public int AttemptCount { get; init; }
    public bool IsPersistent { get; init; } = true;
    public TimeSpan? ExpiresAfter { get; init; }
    public Func<CancellationToken, Task<Result<Unit, NetworkFailure>>> ExecuteAsync { get; init; } = null!;
    public Dictionary<string, object> Metadata { get; init; } = new();
}

public class OperationQueue : IOperationQueue
{
    private readonly OperationQueueConfiguration _config;
    private readonly ConcurrentDictionary<string, QueuedOperation> _operations = new();
    private readonly ConcurrentDictionary<uint, ConcurrentQueue<string>> _connectionQueues = new();
    private readonly SemaphoreSlim _processingLock;
    private readonly Timer _processingTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Lock _queueLock = new();
    private readonly List<Task> _backgroundTasks = [];
    private volatile bool _disposed;

    public OperationQueue(OperationQueueConfiguration config)
    {
        _config = config;
        _processingLock = new SemaphoreSlim(_config.MaxConcurrentOperations);

        _processingTimer = new Timer(ProcessQueuedOperations, null,
            _config.ProcessingInterval, _config.ProcessingInterval);

        Log.Information("Advanced OperationQueue initialized with MaxQueue: {MaxQueue}, MaxConcurrent: {MaxConcurrent}",
            _config.MaxQueueSize, _config.MaxConcurrentOperations);
    }

    public Result<string, NetworkFailure> EnqueueOperation(QueuedOperation operation)
    {
        if (_operations.Count >= _config.MaxQueueSize)
        {
            Log.Warning("Operation queue is full, rejecting operation {OperationType} for connection {ConnectId}",
                operation.Type, operation.ConnectId);
            return Result<string, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Operation queue is full"));
        }

        if (HasSimilarPendingOperation(operation))
        {
            Log.Debug("Similar operation already queued for {OperationType} on connection {ConnectId}",
                operation.Type, operation.ConnectId);
            return Result<string, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Similar operation already pending"));
        }

        lock (_queueLock)
        {
            _operations.TryAdd(operation.Id, operation);

            if (!_connectionQueues.TryGetValue(operation.ConnectId, out ConcurrentQueue<string>? queue))
            {
                queue = new ConcurrentQueue<string>();
                _connectionQueues.TryAdd(operation.ConnectId, queue);
            }

            queue.Enqueue(operation.Id);
        }

        Log.Debug("Queued {OperationType} operation {OperationId} for connection {ConnectId} with priority {Priority}",
            operation.Type, operation.Id, operation.ConnectId, operation.Priority);

        return Result<string, NetworkFailure>.Ok(operation.Id);
    }

    public void ClearConnectionQueue(uint connectId)
    {
        if (_connectionQueues.TryGetValue(connectId, out ConcurrentQueue<string>? queue))
        {
            int removedCount = 0;

            while (queue.TryDequeue(out string? operationId))
            {
                if (_operations.TryRemove(operationId, out _))
                {
                    removedCount++;
                }
            }

            if (queue.IsEmpty)
            {
                _connectionQueues.TryRemove(connectId, out _);
            }

            Log.Information("Cleared {Count} operations for connection {ConnectId}",
                removedCount, connectId);
        }
    }

    public IEnumerable<QueuedOperation> GetPendingOperations(uint? connectId = null)
    {
        IEnumerable<QueuedOperation> operations = _operations.Values;

        if (connectId.HasValue)
        {
            operations = operations.Where(op => op.ConnectId == connectId.Value);
        }

        return operations
            .Where(op => !IsOperationExpired(op))
            .OrderByDescending(op => op.Priority)
            .ThenBy(op => op.QueuedAt)
            .ToList();
    }

    private bool HasSimilarPendingOperation(QueuedOperation newOperation)
    {
        return _operations.Values.Any(existing =>
            existing.ConnectId == newOperation.ConnectId &&
            existing.Type == newOperation.Type &&
            existing.AttemptCount < _config.MaxRetryAttempts);
    }

    private static bool IsOperationExpired(QueuedOperation operation)
    {
        if (!operation.ExpiresAfter.HasValue)
            return false;

        return DateTime.UtcNow - operation.QueuedAt > operation.ExpiresAfter.Value;
    }

    private void CleanupExpiredOperations()
    {
        DateTime now = DateTime.UtcNow;
        List<string> expiredOperationIds = (from kvp in _operations
            let operation = kvp.Value
            let isExpired = IsOperationExpired(operation)
            let isStale =
                operation.LastAttemptAt.HasValue &&
                (now - operation.LastAttemptAt.Value) > _config.StaleOperationThreshold
            where isExpired || isStale
            select kvp.Key).ToList();

        if (expiredOperationIds.Count > 0)
        {
            foreach (string operationId in expiredOperationIds)
            {
                _operations.TryRemove(operationId, out _);
            }

            Log.Debug("Cleaned up {Count} expired/stale operations", expiredOperationIds.Count);
        }

        List<uint> emptyConnections = (from kvp in _connectionQueues where kvp.Value.IsEmpty select kvp.Key).ToList();

        foreach (uint connectId in emptyConnections)
        {
            _connectionQueues.TryRemove(connectId, out _);
        }

        if (emptyConnections.Count > 0)
        {
            Log.Debug("Cleaned up {Count} empty connection queues", emptyConnections.Count);
        }
    }

    private void ProcessQueuedOperations(object? state)
    {
        if (_disposed || _cancellationTokenSource.Token.IsCancellationRequested)
            return;

        Task backgroundTask = Task.Run(async () =>
        {
            try
            {
                await ProcessOperations(_cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during operation queue processing");
            }
        });

        lock (_backgroundTasks)
        {
            if (_disposed) return;
            _backgroundTasks.Add(backgroundTask);
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
        }
    }

    private async Task ProcessOperations(CancellationToken cancellationToken)
    {
        CleanupExpiredOperations();

        List<QueuedOperation> readyOperations = GetReadyOperations();

        if (readyOperations.Count == 0)
            return;

        List<Task> processingTasks = [];

        try
        {
            foreach (QueuedOperation operation in readyOperations.TakeWhile(operation =>
                         !cancellationToken.IsCancellationRequested))
            {
                if (!await _processingLock.WaitAsync(100, cancellationToken)) continue;
                Task processingTask = ProcessSingleOperation(operation, cancellationToken);
                processingTasks.Add(processingTask);
            }

            if (processingTasks.Count > 0)
            {
                await Task.WhenAll(processingTasks);
            }
        }
        finally
        {
            foreach (Task task in processingTasks.Where(task => task.IsCompleted))
            {
                task.Dispose();
            }
        }
    }

    private List<QueuedOperation> GetReadyOperations()
    {
        DateTime now = DateTime.UtcNow;

        return _operations.Values
            .Where(op => !IsOperationExpired(op))
            .Where(op => op.LastAttemptAt == null ||
                         now - op.LastAttemptAt.Value > TimeSpan.FromSeconds(Math.Pow(2, op.AttemptCount)))
            .Where(op => op.AttemptCount < _config.MaxRetryAttempts)
            .OrderByDescending(op => op.Priority)
            .ThenBy(op => op.QueuedAt)
            .Take(_config.MaxConcurrentOperations)
            .ToList();
    }

    private async Task ProcessSingleOperation(QueuedOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            Log.Debug("Processing operation {OperationId} of type {OperationType} (attempt {Attempt})",
                operation.Id, operation.Type, operation.AttemptCount + 1);

            Result<Unit, NetworkFailure> result = await operation.ExecuteAsync(cancellationToken);

            if (result.IsOk)
            {
                _operations.TryRemove(operation.Id, out _);
                Log.Debug("Operation {OperationId} completed successfully", operation.Id);
            }
            else
            {
                await HandleOperationFailure(operation, result.UnwrapErr());
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error processing operation {OperationId}", operation.Id);
            await HandleOperationFailure(operation,
                NetworkFailure.InvalidRequestType($"Processing error: {ex.Message}"));
        }
        finally
        {
            _processingLock.Release();
        }
    }

    private async Task HandleOperationFailure(QueuedOperation operation, NetworkFailure failure)
    {
        int newAttemptCount = operation.AttemptCount + 1;

        if (newAttemptCount >= _config.MaxRetryAttempts)
        {
            _operations.TryRemove(operation.Id, out _);
            Log.Warning("Operation {OperationId} failed permanently after {Attempts} attempts: {Error}",
                operation.Id, newAttemptCount, failure.Message);
            return;
        }

        QueuedOperation updatedOperation = operation with
        {
            AttemptCount = newAttemptCount,
            LastAttemptAt = DateTime.UtcNow
        };

        _operations.TryUpdate(operation.Id, updatedOperation, operation);

        Log.Debug("Operation {OperationId} failed (attempt {Attempt}/{MaxAttempts}): {Error}",
            operation.Id, newAttemptCount, _config.MaxRetryAttempts, failure.Message);

        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _processingTimer.Dispose();
        _cancellationTokenSource.Cancel();

        lock (_backgroundTasks)
        {
            Task completedTasks = Task.WhenAll(_backgroundTasks.Where(t => !t.IsCompleted));
            try
            {
                completedTasks.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            foreach (Task task in _backgroundTasks.Where(t => t.IsCompleted))
            {
                task.Dispose();
            }

            _backgroundTasks.Clear();
        }

        _processingLock.Dispose();
        _cancellationTokenSource.Dispose();

        Log.Information("OperationQueue disposed with {OperationCount} remaining operations",
            _operations.Count);
    }
}