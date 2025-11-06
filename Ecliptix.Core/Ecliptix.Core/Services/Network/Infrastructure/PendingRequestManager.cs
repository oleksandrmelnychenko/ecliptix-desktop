using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Core.Services.Network.Infrastructure;

//TODO: check
public sealed class PendingRequestManager : IPendingRequestManager
{
    private readonly ConcurrentDictionary<string, Func<CancellationToken, Task>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, ITypedPendingRequest> _typedPendingRequests = new();
    private readonly ConcurrentDictionary<string, bool> _retryingRequests = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requestCancellationSources = new();
    private readonly SemaphoreSlim _retryAllSemaphore = new(1, 1);
    private int _pendingCount;

    public event EventHandler<int>? PendingCountChanged;

    public int PendingRequestCount => _pendingCount;

    public void RegisterPendingRequest(string requestId, Func<CancellationToken, Task> retryAction)
    {
        CancellationTokenSource requestCts = new();

        if (!_pendingRequests.TryAdd(requestId, retryAction))
        {
            requestCts.Dispose();
            return;
        }

        if (!_requestCancellationSources.TryAdd(requestId, requestCts))
        {
            _pendingRequests.TryRemove(requestId, out _);
            requestCts.Dispose();
            return;
        }

        int newCount = Interlocked.Increment(ref _pendingCount);
        PendingCountChanged?.Invoke(this, newCount);
    }

    public void RemovePendingRequest(string requestId)
    {
        bool removed = _pendingRequests.TryRemove(requestId, out _) ||
                       _typedPendingRequests.TryRemove(requestId, out _);

        if (_requestCancellationSources.TryRemove(requestId, out CancellationTokenSource? requestCts))
        {
            requestCts.Dispose();
        }

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
            KeyValuePair<string, Func<CancellationToken, Task>>[] untypedRequests = _pendingRequests.ToArray();
            KeyValuePair<string, ITypedPendingRequest>[] typedRequests = _typedPendingRequests.ToArray();
            int successCount = 0;

            Log.Information("Starting retry for {UntypedCount} untyped and {TypedCount} typed pending requests",
                untypedRequests.Length, typedRequests.Length);

            List<KeyValuePair<string, Func<CancellationToken, Task>>> filteredUntypedRequests = [];
            List<KeyValuePair<string, ITypedPendingRequest>> filteredTypedRequests = [];

            foreach (KeyValuePair<string, Func<CancellationToken, Task>> request in untypedRequests)
            {
                if (_retryingRequests.TryAdd(request.Key, true))
                {
                    filteredUntypedRequests.Add(request);
                }
                else if (Log.IsEnabled(LogEventLevel.Debug))
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
                else if (Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("Skipping retry for typed request {RequestId} - already being retried", request.Key);
                }
            }

            Task[] untypedTasks = new Task[filteredUntypedRequests.Count];
            for (int i = 0; i < filteredUntypedRequests.Count; i++)
            {
                KeyValuePair<string, Func<CancellationToken, Task>> request = filteredUntypedRequests[i];
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

    private async Task RetryRequestAsync(string requestId, Func<CancellationToken, Task> retryAction,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? linkedCts = null;
        try
        {
            CancellationToken tokenToUse = cancellationToken;
            if (_requestCancellationSources.TryGetValue(requestId, out CancellationTokenSource? requestCts))
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
                tokenToUse = linkedCts.Token;
            }

            tokenToUse.ThrowIfCancellationRequested();

            Task retryTask = retryAction(tokenToUse);
            if (!retryTask.IsCompleted)
            {
                await retryTask.WaitAsync(tokenToUse).ConfigureAwait(false);
            }
            else
            {
                await retryTask.ConfigureAwait(false);
            }

            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried request {RequestId}", requestId);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Retry for pending request {RequestId} cancelled", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry request {RequestId} - keeping it pending for future retry attempts", requestId);

        }
        finally
        {
            linkedCts?.Dispose();
            _retryingRequests.TryRemove(requestId, out _);
        }
    }

    private async Task RetryTypedRequestAsync(string requestId, ITypedPendingRequest typedRequest,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? linkedCts = null;
        try
        {
            CancellationToken tokenToUse = cancellationToken;
            if (_requestCancellationSources.TryGetValue(requestId, out CancellationTokenSource? requestCts))
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
                tokenToUse = linkedCts.Token;
            }

            await typedRequest.ExecuteAsync(tokenToUse).ConfigureAwait(false);
            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried typed request {RequestId}", requestId);
        }
        catch (OperationCanceledException ex)
        {
            Log.Debug(ex, "Retry for typed pending request {RequestId} cancelled", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry typed request {RequestId} - keeping it pending for future retry attempts", requestId);
        }
        finally
        {
            linkedCts?.Dispose();
            _retryingRequests.TryRemove(requestId, out _);
        }
    }
}
