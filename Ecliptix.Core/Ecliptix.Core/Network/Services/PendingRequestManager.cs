using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Ecliptix.Core.Network.Services;

public interface IPendingRequestManager
{
    void RegisterPendingRequest(string requestId, Func<Task> retryAction);
    void RegisterPendingRequest<T>(string requestId, Func<Task<T>> retryAction, TaskCompletionSource<T> originalTaskCompletionSource);
    void RemovePendingRequest(string requestId);
    Task<int> RetryAllPendingRequestsAsync(CancellationToken cancellationToken = default);
    void CancelAllPendingRequests();
    int PendingRequestCount { get; }
    event EventHandler<int>? PendingCountChanged;
}

public class PendingRequestManager : IPendingRequestManager
{
    private readonly ConcurrentDictionary<string, Func<Task>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, object> _typedPendingRequests = new();
    private readonly ConcurrentDictionary<string, bool> _retryingRequests = new(); // Track currently retrying requests
    private readonly SemaphoreSlim _retryAllSemaphore = new(1, 1); // Prevent multiple simultaneous retryAll calls
    private int _pendingCount;
    
    public event EventHandler<int>? PendingCountChanged;
    
    public int PendingRequestCount => _pendingCount;
    
    public PendingRequestManager()
    {
    }
    
    public void RegisterPendingRequest(string requestId, Func<Task> retryAction)
    {
        if (_pendingRequests.TryAdd(requestId, retryAction))
        {
            int newCount = Interlocked.Increment(ref _pendingCount);
            PendingCountChanged?.Invoke(this, newCount);
        }
    }
    
    public void RegisterPendingRequest<T>(string requestId, Func<Task<T>> retryAction, TaskCompletionSource<T> originalTaskCompletionSource)
    {
        var typedRequest = new TypedPendingRequest<T>(retryAction, originalTaskCompletionSource);
        if (_typedPendingRequests.TryAdd(requestId, typedRequest))
        {
            int newCount = Interlocked.Increment(ref _pendingCount);
            PendingCountChanged?.Invoke(this, newCount);
        }
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
        // CRITICAL FIX: Single-flight protection to prevent duplicate retry operations
        await _retryAllSemaphore.WaitAsync(cancellationToken);
        try
        {
            var untypedRequests = _pendingRequests.ToArray();
            var typedRequests = _typedPendingRequests.ToArray();
            int successCount = 0;
            
            Log.Information("Starting retry for {UntypedCount} untyped and {TypedCount} typed pending requests", 
                untypedRequests.Length, typedRequests.Length);
            
            // Filter out requests that are already being retried
            var filteredUntypedRequests = new List<KeyValuePair<string, Func<Task>>>();
            var filteredTypedRequests = new List<KeyValuePair<string, object>>();
            
            foreach (var request in untypedRequests)
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
            
            foreach (var request in typedRequests)
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
                var request = filteredUntypedRequests[i];
                untypedTasks[i] = RetryRequestAsync(request.Key, request.Value, cancellationToken);
            }
            
            Task[] typedTasks = new Task[filteredTypedRequests.Count];
            for (int i = 0; i < filteredTypedRequests.Count; i++)
            {
                var request = filteredTypedRequests[i];
                typedTasks[i] = RetryTypedRequestAsync(request.Key, request.Value, cancellationToken);
            }
            
            var allTasks = new Task[untypedTasks.Length + typedTasks.Length];
            Array.Copy(untypedTasks, allTasks, untypedTasks.Length);
            Array.Copy(typedTasks, 0, allTasks, untypedTasks.Length, typedTasks.Length);
            
            if (allTasks.Length > 0)
            {
                await Task.WhenAll(allTasks);
                
                foreach (Task task in allTasks)
                {
                    if (task.IsCompletedSuccessfully)
                        successCount++;
                }
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
            await retryAction();
            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry request {RequestId}", requestId);
        }
        finally
        {
            // Always clear retrying flag regardless of success/failure
            _retryingRequests.TryRemove(requestId, out _);
        }
    }
    
    [UnconditionalSuppressMessage("Trimming", "IL2075:Unrecognized reflection pattern", Justification = "ExecuteAsync method is guaranteed to exist on TypedPendingRequest<T>")]
    private async Task RetryTypedRequestAsync(string requestId, object typedRequest, CancellationToken cancellationToken)
    {
        try
        {
            var method = typedRequest.GetType().GetMethod("ExecuteAsync");
            if (method != null)
            {
                var task = (Task)method.Invoke(typedRequest, new object[] { cancellationToken });
                await task;
            }
            RemovePendingRequest(requestId);
            Log.Debug("Successfully retried typed request {RequestId}", requestId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retry typed request {RequestId}", requestId);
        }
        finally
        {
            // Always clear retrying flag regardless of success/failure
            _retryingRequests.TryRemove(requestId, out _);
        }
    }
    
    [UnconditionalSuppressMessage("Trimming", "IL2075:Unrecognized reflection pattern", Justification = "Cancel method is guaranteed to exist on TypedPendingRequest<T>")]
    public void CancelAllPendingRequests()
    {
        int count = _pendingRequests.Count + _typedPendingRequests.Count;
        _pendingRequests.Clear();
        
        foreach (var typedRequest in _typedPendingRequests.Values)
        {
            if (typedRequest.GetType().GetMethod("Cancel") is var cancelMethod && cancelMethod != null)
            {
                cancelMethod.Invoke(typedRequest, null);
            }
        }
        _typedPendingRequests.Clear();
        
        // Also clear retrying requests since all are cancelled
        _retryingRequests.Clear();
        
        Interlocked.Exchange(ref _pendingCount, 0);
        PendingCountChanged?.Invoke(this, 0);
    }
}

internal class TypedPendingRequest<T>
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
            var result = await _retryAction();
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