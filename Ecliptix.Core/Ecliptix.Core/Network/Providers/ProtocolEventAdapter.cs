using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Protocol.System.Core;
using Serilog;

namespace Ecliptix.Core.Network.Providers;

public class ProtocolEventAdapter(IProtocolStateCallbacks? stateCallbacks) : IProtocolEventHandler, IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly List<Task> _backgroundTasks = new();
    private readonly Lock _tasksLock = new();

    public void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex)
    {
        if (stateCallbacks != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            Task task = ExecuteCallbackSafely(
                () => stateCallbacks.OnDhRatchetPerformed(connectId, isSending, newIndex),
                "DH ratchet callback",
                _cancellationTokenSource.Token);

            TrackBackgroundTask(task);
        }
    }

    public void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength)
    {
        if (stateCallbacks != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            Task task = ExecuteCallbackSafely(
                () => stateCallbacks.OnChainSynchronized(connectId, localLength, remoteLength),
                "chain sync callback",
                _cancellationTokenSource.Token);

            TrackBackgroundTask(task);
        }
    }

    public void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys)
    {
        if (stateCallbacks != null && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            Task task = ExecuteCallbackSafely(
                () => stateCallbacks.OnMessageReceived(connectId, messageIndex, hasSkippedKeys),
                "message processed callback",
                _cancellationTokenSource.Token);

            TrackBackgroundTask(task);
        }
    }

    private async Task ExecuteCallbackSafely(Func<Task> callback, string callbackName,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(callback, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in {CallbackName}", callbackName);
        }
    }

    private void TrackBackgroundTask(Task task)
    {
        lock (_tasksLock)
        {
            _backgroundTasks.Add(task);
            _backgroundTasks.RemoveAll(t => t.IsCompleted);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();

        try
        {
            Task[] tasksToWait;
            lock (_tasksLock)
            {
                tasksToWait = _backgroundTasks.Where(t => !t.IsCompleted).ToArray();
            }

            if (tasksToWait.Length > 0)
            {
                Task.WaitAll(tasksToWait, TimeSpan.FromSeconds(5));
            }
        }
        catch
        {
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }
}