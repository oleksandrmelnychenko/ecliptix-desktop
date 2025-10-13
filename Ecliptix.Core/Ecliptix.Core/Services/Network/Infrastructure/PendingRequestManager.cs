using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Ecliptix.Core.Services.Network.Infrastructure;

internal interface ITypedPendingRequest
{
    Task ExecuteAsync(CancellationToken cancellationToken);
    void Cancel();
}

public interface IPendingRequestManager
{
    void RegisterPendingRequest(string requestId, Func<Task> retryAction);

    void RegisterPendingRequest<T>(string requestId, Func<Task<T>> retryAction,
        TaskCompletionSource<T> originalTaskCompletionSource);

    void RemovePendingRequest(string requestId);
    Task<int> RetryAllPendingRequestsAsync(CancellationToken cancellationToken = default);
    void CancelAllPendingRequests();
    int PendingRequestCount { get; }
    event EventHandler<int>? PendingCountChanged;
}

public class PendingRequestManager : IPendingRequestManager
{
    private readonly ConcurrentDictionary<string, Func<Task>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, ITypedPendingRequest> _typedPendingRequests = new();
    private readonly ConcurrentDictionary<string, bool> _retryingRequests = new();
    private readonly SemaphoreSlim _retryAllSemaphore = new(1, 1);
    private int _pendingCount;

    public event EventHandler<int>? PendingCountChanged;

    public int PendingRequestCount => _pendingCount;

    public void RegisterPendingRequest(string requestId, Func<Task> retryAction)
    {
        if (_pendingRequests.TryAdd(requestId, retryAction))
        {
            int newCount = Interlocked.Increment(ref _pendingCount);
            PendingCountChanged?.Invoke(this, newCount);
        }
    }

    public void RegisterPendingRequest<T>(string requestId, Func<Task<T>> retryAction,
        TaskCompletionSource<T> originalTaskCompletionSource)
    {
        TypedPendingRequest<T> typedRequest = new(retryAction, originalTaskCompletionSource);
        if (!_typedPendingRequests.TryAdd(requestId, typedRequest)) return;
        int newCount = Interlocked.Increment(ref _pendingCount);
        PendingCountChanged?.Invoke(this, newCount);
    }

    public void RemovePendingRequest(string requestId)
    {
        bool removed = _pendingRequests.TryRemove(requestId, out _) ||
                       _typedPendingRequests.TryRemove(requestId, out _);

        if (removed)
        {
            int newCount = Interlocked.Decrement(ref _pendingCount);
            PendingCountChanged?.Invoke(this, newCount);
        }
    }

    public async Task<int> RetryAllPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        await _retryAllSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            KeyValuePair<string, Func<Task>>[] untypedRequests = _pendingRequests.ToArray();
            KeyValuePair<string, ITypedPendingRequest>[] typedRequests = _typedPendingRequests.ToArray();
            int successCount = 0;

            Log.Information("Starting retry for {UntypedCount} untyped and {TypedCount} typed pending requests",
                untypedRequests.Length, typedRequests.Length);

            List<KeyValuePair<string, Func<Task>>> filteredUntypedRequests = [];
            List<KeyValuePair<string, ITypedPendingRequest>> filteredTypedRequests = [];

            foreach (KeyValuePair<string, Func<Task>> request in untypedRequests)
            {
                if (_retryingRequests.TryAdd(request.Key, true))
                {
                    filteredUntypedRequests.Add(request);
                }
                else
                {
                    Log.Debug("Skipping retry for request {RequestId} - already being retried", request.Key);
                }
            }

            foreach (KeyValuePair<string, ITypedPendingRequest> request in typedRequests)
            {
                if (_retryingRequests.TryAdd(request.Key, true))
                {
                    filteredTypedRequests.Add(request);
                }
                else
                {
                    Log.Debug("Skipping retry for typed request {RequestId} - already being retried", request.Key);
                }
            }

            Task[] untypedTasks = new Task[filteredUntypedRequests.Count];
            for (int i = 0; i < filteredUntypedRequests.Count; i++)
            {
                KeyValuePair<string, Func<Task>> request = filteredUntypedRequests[i];
                untypedTasks[i] = RetryRequestAsync(request.Key, request.Value, cancellationToken);
            }

            Task[] typedTasks = new Task[filteredTypedRequests.Count];
            for (int i = 0; i < filteredTypedRequests.Count; i++)
            {
                KeyValuePair<string, ITypedPendingRequest> request = filteredTypedRequests[i];
                typedTasks[i] = RetryTypedRequestAsync(request.Key, request.Value, cancellationToken);
            }

            Task[] allTasks = new Task[untypedTasks.Length + typedTasks.Length];
            Array.Copy(untypedTasks, allTasks, untypedTasks.Length);
            Array.Copy(typedTasks, 0, allTasks, untypedTasks.Length, typedTasks.Length);

            if (allTasks.Length > 0)
            {
                await Task.WhenAll(allTasks).ConfigureAwait(false);

                successCount += allTasks.Count(task => task.IsCompletedSuccessfully);
            }

            Log.Information("Completed retry operation: {SuccessCount}/{TotalCount} requests succeeded",
                successCount, allTasks.Length);

            return successCount;
        }
        finally
        {
            _retryAllSemaphore.Release();
        }
    }

    private async Task RetryRequestAsync(string requestId, Func<Task> retryAction, CancellationToken cancellationToken)
    {
        try
        {
            await retryAction().ConfigureAwait(false);
            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry request {RequestId} - keeping it pending for future retry attempts", requestId);

        }
        finally
        {
            _retryingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task RetryTypedRequestAsync(string requestId, ITypedPendingRequest typedRequest,
        CancellationToken cancellationToken)
    {
        try
        {
            await typedRequest.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried typed request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry typed request {RequestId} - keeping it pending for future retry attempts", requestId);
        }
        finally
        {
            _retryingRequests.TryRemove(requestId, out _);
        }
    }

    public void CancelAllPendingRequests()
    {
        int count = _pendingRequests.Count + _typedPendingRequests.Count;
        _pendingRequests.Clear();

        foreach (ITypedPendingRequest typedRequest in _typedPendingRequests.Values)
        {
            typedRequest.Cancel();
        }

        _typedPendingRequests.Clear();
        _retryingRequests.Clear();

        Interlocked.Exchange(ref _pendingCount, 0);
        PendingCountChanged?.Invoke(this, 0);
    }
}

internal class TypedPendingRequest<T> : ITypedPendingRequest
{
    private readonly Func<Task<T>> _retryAction;
    private readonly TaskCompletionSource<T> _originalTaskCompletionSource;

    public TypedPendingRequest(Func<Task<T>> retryAction, TaskCompletionSource<T> originalTaskCompletionSource)
    {
        _retryAction = retryAction;
        _originalTaskCompletionSource = originalTaskCompletionSource;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            T result = await _retryAction().ConfigureAwait(false);
            _originalTaskCompletionSource.SetResult(result);
        }
        catch (Exception ex)
        {
            _originalTaskCompletionSource.SetException(ex);
            throw;
        }
    }

    public void Cancel()
    {
        _originalTaskCompletionSource.SetCanceled();
    }
}
