using System.Collections.Concurrent;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed partial class NetworkProvider
{
    private static TaskCompletionSource<bool> CreateOutageTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly NativeProtocolSessionManager _nativeSessions = new();
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeStreams = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRequests = new();
    private readonly Lock _cancellationLock = new();
    private readonly CancellationTokenSource _shutdownCancellationToken = new();
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _channelGates = new();
    private readonly SemaphoreSlim _retryPendingRequestsGate = new(1, 1);
    private readonly Lock _outageLock = new();
    private readonly Lock _disposeLock = new();
    private readonly Lock _appInstanceSetterLock = new();
    private readonly Lock _nativeInitLock = new();
    private readonly ConcurrentDictionary<uint, Task> _pendingPersistTasks = new();
    private bool _nativeInitialized;
    private bool _nativeVersionLogged;

    private CancellationTokenSource? _connectionRecoveryCts;
    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;
    private int _outageState;
    private TaskCompletionSource<bool> _outageCompletionSource = CreateOutageTcs();
    private volatile bool _disposed;

    private RequestPipeline? _requests;

    private RequestPipeline Requests => _requests ??= new RequestPipeline(this);
}
