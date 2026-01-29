using System.Collections.Concurrent;
using Avalonia.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Network.Infrastructure.Network.Core.Constants;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed partial class NetworkProvider
{
    public async Task<Result<Unit, NetworkFailure>> ExecuteUnaryRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false,
        CancellationToken token = default,
        bool waitForRecovery = true,
        RpcRequestContext? requestContext = null)
    {
        return await Requests.ExecuteUnaryRequestAsync(
                connectId,
                serviceType,
                plainBuffer,
                onCompleted,
                allowDuplicates,
                token,
                waitForRecovery,
                requestContext)
            .ConfigureAwait(false);
    }

    public async Task<Result<Unit, NetworkFailure>> ExecuteReceiveStreamRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        bool allowDuplicates = false,
        CancellationToken token = default)
    {
        return await Requests.ExecuteReceiveStreamRequestAsync(
                connectId,
                serviceType,
                plainBuffer,
                onStreamItem,
                allowDuplicates,
                token)
            .ConfigureAwait(false);
    }

    private sealed class RequestPipeline
    {
        private readonly NetworkProvider _provider;

        private NetworkProviderDependencies Dependencies => _provider._dependencies;
        private NetworkProviderServices Services => _provider._services;
        private NetworkProviderSecurity Security => _provider._security;

        private NativeProtocolSessionManager NativeSessions => _provider._nativeSessions;
        private ConcurrentDictionary<uint, CancellationTokenSource> ActiveStreams => _provider._activeStreams;
        private ConcurrentDictionary<string, CancellationTokenSource> PendingRequests => _provider._pendingRequests;
        private CancellationTokenSource ShutdownCancellationToken => _provider._shutdownCancellationToken;
        private Lock OutageLock => _provider._outageLock;
        private ref int OutageState => ref _provider._outageState;
        private TaskCompletionSource<bool> OutageCompletionSource => _provider._outageCompletionSource;

        internal RequestPipeline(NetworkProvider provider)
        {
            _provider = provider;
        }

        internal async Task<Result<Unit, NetworkFailure>> ExecuteUnaryRequestAsync(
            uint connectId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
            bool allowDuplicates,
            CancellationToken token,
            bool waitForRecovery,
            RpcRequestContext? requestContext)
        {
            ServiceRequestParams request = new(
                ConnectId: connectId,
                ServiceType: serviceType,
                PlainBuffer: plainBuffer,
                FlowType: ServiceFlowType.SINGLE,
                OnCompleted: onCompleted,
                RequestContext: requestContext,
                AllowDuplicateRequests: allowDuplicates,
                WaitForRecovery: waitForRecovery,
                CancellationToken: token);

            return await ExecuteServiceRequestInternalAsync(request).ConfigureAwait(false);
        }

        internal async Task<Result<Unit, NetworkFailure>> ExecuteReceiveStreamRequestAsync(
            uint connectId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
            bool allowDuplicates,
            CancellationToken token)
        {
            ServiceRequestParams request = new(
                ConnectId: connectId,
                ServiceType: serviceType,
                PlainBuffer: plainBuffer,
                FlowType: ServiceFlowType.RECEIVE_STREAM,
                OnCompleted: onStreamItem,
                RequestContext: null,
                AllowDuplicateRequests: allowDuplicates,
                WaitForRecovery: true,
                CancellationToken: token);

            return await ExecuteServiceRequestInternalAsync(request).ConfigureAwait(false);
        }

        private async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestInternalAsync(
            ServiceRequestParams request)
        {
            RpcRequestContext effectiveContext = request.RequestContext ?? RpcRequestContext.CreateNew();
            RetryBehavior retryBehavior = Security.RetryPolicyProvider.GetRetryBehavior(request.ServiceType);

            string requestKey = GenerateRequestKey(request.ConnectId, request.ServiceType, request.PlainBuffer);
            bool shouldAllowDuplicates =
                request.AllowDuplicateRequests || CanServiceTypeBeDuplicated(request.ServiceType);

            Result<Unit, NetworkFailure>? duplicateCheckResult =
                TryRegisterRequest(requestKey, shouldAllowDuplicates, out CancellationTokenSource requestCts);
            if (duplicateCheckResult.HasValue)
            {
                return duplicateCheckResult.Value;
            }

            using RequestCancellationContext cancellationContext =
                new(request.CancellationToken, requestCts, shouldAllowDuplicates, requestKey, PendingRequests);

            try
            {
                ServiceRequestContext serviceContext = new()
                {
                    ConnectId = request.ConnectId,
                    ServiceType = request.ServiceType,
                    PlainBuffer = request.PlainBuffer,
                    FlowType = request.FlowType,
                    OnCompleted = request.OnCompleted,
                    RequestContext = effectiveContext,
                    RetryBehavior = retryBehavior
                };

                return await ExecuteRequestWithProtocolAsync(
                        serviceContext, request.WaitForRecovery, cancellationContext.OperationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.OperationCancelled("Request cancelled by caller"));
            }
            catch (OperationCanceledException) when (request.FlowType == ServiceFlowType.RECEIVE_STREAM)
            {
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
            catch (OperationCanceledException)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(
                        "Request cancelled due to network timeout or connection failure"));
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.OperationCancelled("Request cancelled via gRPC protocol"));
            }

            catch (Exception ex)
            {
                return Result<Unit, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(ex.Message));
            }
        }

        private static string GenerateRequestKey(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
        {
            if (serviceType is RpcServiceType.SignInInitRequest or RpcServiceType.SignInCompleteRequest)
            {
                return $"{connectId}_{serviceType}_auth_operation";
            }

            int bytesToHash = Math.Min(plainBuffer.Length, NetworkConstants.Protocol.REQUEST_KEY_HEX_PREFIX_LENGTH / 2);
            Span<char> hexBuffer = stackalloc char[NetworkConstants.Protocol.REQUEST_KEY_HEX_PREFIX_LENGTH];
            bool success = Convert.TryToHexString(plainBuffer.AsSpan(0, bytesToHash), hexBuffer, out int charsWritten);
            return success
                ? $"{connectId}_{serviceType}_{hexBuffer[..charsWritten].ToString()}"
                : $"{connectId}_{serviceType}_fallback";
        }

        private Result<Unit, NetworkFailure>? TryRegisterRequest(
            string requestKey,
            bool shouldAllowDuplicates,
            out CancellationTokenSource cancellationTokenSource)
        {
            cancellationTokenSource = new CancellationTokenSource();

            if (shouldAllowDuplicates)
            {
                return null;
            }

            if (PendingRequests.TryAdd(requestKey, cancellationTokenSource))
            {
                return null;
            }

            cancellationTokenSource.Dispose();
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Duplicate request rejected"));
        }

        private async Task<Result<Unit, NetworkFailure>> ExecuteRequestWithProtocolAsync(
            ServiceRequestContext requestContext,
            bool waitForRecovery,
            CancellationToken operationToken)
        {
            await WaitForOutageRecoveryAsync(operationToken, waitForRecovery).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();

            if (NativeSessions.Get(requestContext.ConnectId).IsErr)
            {
                return HandleMissingConnection();
            }

            uint logicalOperationId = GenerateLogicalOperationId(
                requestContext.ConnectId, requestContext.ServiceType, requestContext.PlainBuffer);

            Result<Unit, NetworkFailure> networkResult = await ExecuteServiceFlowAsync(
                    logicalOperationId, requestContext, operationToken)
                .ConfigureAwait(false);

            if (networkResult.IsOk && Volatile.Read(ref OutageState) == 1)
            {
                _provider.ExitOutage();
            }

            return networkResult;
        }

        private Result<Unit, NetworkFailure> HandleMissingConnection()
        {
            NetworkFailure noConnectionFailure = NetworkFailure.DataCenterNotResponding(
                "Connection unavailable - server may be recovering");

            _ = Services.ConnectivityService.PublishAsync(
                ConnectivityIntent.ServerShutdown(noConnectionFailure)).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception,
                            "[NETWORK-PROVIDER] Unhandled exception publishing server shutdown event");
                    }
                },
                TaskScheduler.Default);

            return Result<Unit, NetworkFailure>.Err(noConnectionFailure);
        }

        private async Task<Result<Unit, NetworkFailure>> ExecuteServiceFlowAsync(
            uint logicalOperationId,
            ServiceRequestContext requestContext,
            CancellationToken operationToken)
        {
            return requestContext.FlowType switch
            {
                ServiceFlowType.SINGLE => await SendUnaryRequestAsync(
                        logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                        requestContext.FlowType, requestContext.OnCompleted, requestContext.ConnectId,
                        requestContext.RetryBehavior, operationToken)
                    .ConfigureAwait(false),
                ServiceFlowType.RECEIVE_STREAM => await SendReceiveStreamRequestAsync(
                        logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                        requestContext.FlowType, requestContext.RequestContext, requestContext.OnCompleted,
                        requestContext.RetryBehavior, requestContext.ConnectId, operationToken)
                    .ConfigureAwait(false),
                ServiceFlowType.SEND_STREAM => await SendSendStreamRequestAsync(
                        logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                        requestContext.FlowType, requestContext.RequestContext, requestContext.ConnectId,
                        operationToken)
                    .ConfigureAwait(false),
                ServiceFlowType.BIDIRECTIONAL_STREAM => await SendBidirectionalStreamRequestAsync(
                        logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                        requestContext.FlowType, requestContext.RequestContext, requestContext.ConnectId,
                        operationToken)
                    .ConfigureAwait(false),
                _ => Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"Unsupported flow type: {requestContext.FlowType}"))
            };
        }

        private readonly struct ServiceRequestContext
        {
            public required uint ConnectId { get; init; }
            public required RpcServiceType ServiceType { get; init; }
            public required byte[] PlainBuffer { get; init; }
            public required ServiceFlowType FlowType { get; init; }
            public required Func<byte[], Task<Result<Unit, NetworkFailure>>> OnCompleted { get; init; }
            public required RpcRequestContext RequestContext { get; init; }
            public required RetryBehavior RetryBehavior { get; init; }
        }

        private readonly struct RequestCancellationContext : IDisposable
        {
            private readonly CancellationTokenRegistration _tokenRegistration;
            private readonly CancellationTokenSource _linkedCts;
            private readonly CancellationTokenSource _requestCts;
            private readonly bool _shouldAllowDuplicates;
            private readonly string _requestKey;
            private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRequests;

            public CancellationToken OperationToken { get; }

            public RequestCancellationContext(
                CancellationToken cancellationToken,
                CancellationTokenSource requestCts,
                bool shouldAllowDuplicates,
                string requestKey,
                ConcurrentDictionary<string, CancellationTokenSource> pendingRequests)
            {
                _requestCts = requestCts;
                _shouldAllowDuplicates = shouldAllowDuplicates;
                _requestKey = requestKey;
                _pendingRequests = pendingRequests;

                _tokenRegistration = cancellationToken.CanBeCanceled
                    ? cancellationToken.Register(() =>
                    {
                        try
                        {
                            if (!requestCts.IsCancellationRequested)
                            {
                                requestCts.Cancel();
                            }
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    })
                    : default;

                _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, requestCts.Token);
                OperationToken = _linkedCts.Token;
            }

            public void Dispose()
            {
                _tokenRegistration.Dispose();
                _linkedCts.Dispose();

                if (_shouldAllowDuplicates)
                {
                    _requestCts.Dispose();
                    return;
                }

                if (_pendingRequests.TryRemove(_requestKey, out CancellationTokenSource? pendingCts))
                {
                    pendingCts.Dispose();
                }
                else
                {
                    _requestCts.Dispose();
                }
            }
        }

        private async Task<Result<Unit, NetworkFailure>> SendUnaryRequestAsync(
            uint logicalOperationId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            ServiceFlowType flowType,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
            uint connectId,
            RetryBehavior retryBehavior,
            CancellationToken token)
        {
            bool shouldUseRetry = retryBehavior.ShouldRetry;

            Result<RpcFlow, NetworkFailure> invokeResult;
            RpcRequestContext? lastRequestContext = null;

            if (shouldUseRetry)
            {
                string stableIdempotencyKey = Guid.NewGuid().ToString("N");
                string stableCorrelationId = Guid.NewGuid().ToString("N");
                RpcRequestContext encryptionContext =
                    RpcRequestContext.CreateWithIds(stableCorrelationId, stableIdempotencyKey, 1);

                Result<SecureEnvelope, NetworkFailure> encryptResult =
                    _provider.EncryptPayload(
                        connectId,
                        logicalOperationId,
                        EnvelopeType.Request,
                        plainBuffer,
                        encryptionContext.CorrelationId);

                if (encryptResult.IsErr)
                {
                    return Result<Unit, NetworkFailure>.Err(encryptResult.UnwrapErr());
                }

                SecureEnvelope encryptedPayload = encryptResult.Unwrap();

                invokeResult = await Services.RetryStrategy.ExecuteRpcOperationAsync(
                    (attempt, ct) =>
                    {
                        RpcRequestContext attemptContext =
                            RpcRequestContext.CreateWithIds(stableCorrelationId, stableIdempotencyKey, attempt);
                        lastRequestContext = attemptContext;

                        ServiceRequest request = ServiceRequest.New(
                            logicalOperationId,
                            flowType,
                            serviceType,
                            encryptedPayload,
                            [],
                            attemptContext);

                        return Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, ct);
                    },
                    $"UnaryRequest_{serviceType}",
                    connectId,
                    serviceType: serviceType,
                    maxRetries: Math.Max(0, retryBehavior.MaxAttempts - 1),
                    cancellationToken: token).ConfigureAwait(false);
            }
            else
            {
                RpcRequestContext singleAttemptContext = RpcRequestContext.CreateNew();
                lastRequestContext = singleAttemptContext;

                Result<ServiceRequest, NetworkFailure> serviceRequestResult = _provider.BuildRequestWithId(
                    connectId, logicalOperationId, serviceType, plainBuffer, flowType, singleAttemptContext);

                if (serviceRequestResult.IsErr)
                {
                    return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
                }

                ServiceRequest request = serviceRequestResult.Unwrap();
                invokeResult = await Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token)
                    .ConfigureAwait(false);
            }

            if (invokeResult.IsErr)
            {
                NetworkFailure failure = AttachCorrelation(invokeResult.UnwrapErr(), lastRequestContext);
                failure = ApplyReinitIfNeeded(failure, serviceType, retryBehavior);
                return Result<Unit, NetworkFailure>.Err(failure);
            }

            RpcFlow flow = invokeResult.Unwrap();
            if (flow is not RpcFlow.SingleCall singleCall)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"Expected SingleCall flow but received {flow.GetType().Name}"));
            }

            Result<SecureEnvelope, NetworkFailure> callResult = await singleCall.Result.ConfigureAwait(false);
            if (callResult.IsErr)
            {
                NetworkFailure failure = AttachCorrelation(callResult.UnwrapErr(), lastRequestContext);
                failure = ApplyReinitIfNeeded(failure, serviceType, retryBehavior);
                return Result<Unit, NetworkFailure>.Err(failure);
            }

            SecureEnvelope inboundPayload = callResult.Unwrap();

            Result<byte[], NetworkFailure> decryptedData =
                _provider.DecryptPayload(connectId, inboundPayload);
            if (decryptedData.IsErr)
            {
                Log.Error("[CLIENT-DECRYPT-ERROR] Decryption failed. ERROR: {Error}",
                    decryptedData.UnwrapErr().Message);
                NetworkFailure decryptFailure = decryptedData.UnwrapErr();

                if (FailureClassification.IsProtocolStateMismatch(decryptFailure))
                {
                    Log.Warning(
                        "[NETWORK-PROVIDER] Protocol state mismatch during message decryption. Cleaning up stale state. ConnectId: {ConnectId}",
                        connectId);

                    await _provider.CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);

                    NativeSessions.Remove(connectId);
                }

                decryptFailure = ApplyReinitIfNeeded(decryptFailure, serviceType, retryBehavior);
                return Result<Unit, NetworkFailure>.Err(decryptFailure);
            }

            await onCompleted(decryptedData.Unwrap()).ConfigureAwait(false);
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        private static NetworkFailure AttachCorrelation(NetworkFailure failure, RpcRequestContext? context)
        {
            if (context == null)
            {
                return failure;
            }

            if (failure.UserError is { } userError && string.IsNullOrWhiteSpace(userError.CorrelationId))
            {
                return failure with { UserError = userError with { CorrelationId = context.CorrelationId } };
            }

            return failure;
        }

        private static bool IsCompleteOperation(RpcServiceType serviceType)
        {
            return serviceType is
                RpcServiceType.RegistrationComplete or
                RpcServiceType.RecoveryComplete or
                RpcServiceType.SignInCompleteRequest;
        }

        private static bool ShouldReinitOnFailure(NetworkFailure failure)
        {
            return failure.FailureType is
                NetworkFailureType.DATA_CENTER_NOT_RESPONDING or
                NetworkFailureType.DATA_CENTER_SHUTDOWN or
                NetworkFailureType.PROTOCOL_STATE_MISMATCH;
        }

        private static NetworkFailure ApplyReinitIfNeeded(
            NetworkFailure failure,
            RpcServiceType serviceType,
            RetryBehavior retryBehavior)
        {
            if (IsCompleteOperation(serviceType) &&
                retryBehavior.ReinitOnCompleteFailure &&
                ShouldReinitOnFailure(failure))
            {
                return failure with { RequiresReinit = true };
            }

            return failure;
        }

        private async Task<Result<Unit, NetworkFailure>> SendReceiveStreamRequestAsync(
            uint logicalOperationId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            ServiceFlowType flowType,
            RpcRequestContext requestContext,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
            RetryBehavior retryBehavior,
            uint connectId,
            CancellationToken token)
        {
            if (retryBehavior.ShouldRetry)
            {
                string stableCorrelationId = requestContext.CorrelationId;
                string stableIdempotencyKey = requestContext.IdempotencyKey;
                RpcRequestContext encryptionContext =
                    RpcRequestContext.CreateWithIds(stableCorrelationId, stableIdempotencyKey, 1);

                Result<SecureEnvelope, NetworkFailure> encryptResult =
                    _provider.EncryptPayload(
                        connectId,
                        logicalOperationId,
                        EnvelopeType.Request,
                        plainBuffer,
                        encryptionContext.CorrelationId);

                if (encryptResult.IsErr)
                {
                    return Result<Unit, NetworkFailure>.Err(encryptResult.UnwrapErr());
                }

                SecureEnvelope encryptedPayload = encryptResult.Unwrap();

                return await Services.RetryStrategy.ExecuteRpcOperationAsync(
                    async (attempt, ct) =>
                    {
                        RpcRequestContext attemptContext =
                            RpcRequestContext.CreateWithIds(stableCorrelationId, stableIdempotencyKey, attempt);

                        ServiceRequest request = ServiceRequest.New(
                            logicalOperationId,
                            flowType,
                            serviceType,
                            encryptedPayload,
                            [],
                            attemptContext);

                        Result<Unit, NetworkFailure> processResult = await ProcessStreamWithRequest(
                            request, onStreamItem, connectId, ct).ConfigureAwait(false);

                        return processResult;
                    },
                    $"StreamRequest_{serviceType}",
                    connectId,
                    serviceType: serviceType,
                    maxRetries: Math.Max(0, retryBehavior.MaxAttempts - 1),
                    cancellationToken: token).ConfigureAwait(false);
            }

            Result<ServiceRequest, NetworkFailure> serviceRequestResult = _provider.BuildRequestWithId(
                connectId, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

            if (serviceRequestResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
            }

            ServiceRequest request = serviceRequestResult.Unwrap();
            return await ProcessStreamWithRequest(request, onStreamItem, connectId, token)
                .ConfigureAwait(false);
        }

        private async Task<Result<Unit, NetworkFailure>> ProcessStreamWithRequest(
            ServiceRequest request,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
            uint connectId,
            CancellationToken token)
        {
            if (connectId == 0)
            {
                return await ProcessStreamDirectly(request, onStreamItem, connectId, token)
                    .ConfigureAwait(false);
            }

            using CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            ActiveStreams.TryAdd(connectId, linkedTokenSource);

            Result<RpcFlow, NetworkFailure> invokeResult =
                await Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, linkedTokenSource.Token)
                    .ConfigureAwait(false);

            if (invokeResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
            }

            Result<RpcFlow.InboundStream, NetworkFailure> streamResult = ValidateStreamFlow(invokeResult.Unwrap());
            if (streamResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(streamResult.UnwrapErr());
            }

            RpcFlow.InboundStream inboundStream = streamResult.Unwrap();

            try
            {
                await foreach (Result<SecureEnvelope, NetworkFailure> streamItem in
                               inboundStream.Stream.WithCancellation(linkedTokenSource.Token))
                {
                    if (streamItem.IsErr)
                    {
                        NetworkFailure failure = streamItem.UnwrapErr();
                        NotifyStreamError(failure, connectId);
                        return Result<Unit, NetworkFailure>.Err(failure);
                    }

                    SecureEnvelope streamPayload = streamItem.Unwrap();
                    await ProcessStreamItemAsync(streamPayload, connectId, onStreamItem).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (linkedTokenSource.Token.IsCancellationRequested)
            {
            }
            finally
            {
                CleanupActiveStream(connectId);
            }

            NotifyStreamSuccess(connectId);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        private static Result<RpcFlow.InboundStream, NetworkFailure> ValidateStreamFlow(RpcFlow flow)
        {
            if (flow is not RpcFlow.InboundStream inboundStream)
            {
                return Result<RpcFlow.InboundStream, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(
                        $"Expected InboundStream flow but received {flow.GetType().Name}"));
            }

            return Result<RpcFlow.InboundStream, NetworkFailure>.Ok(inboundStream);
        }

        private void NotifyStreamError(NetworkFailure failure, uint connectId)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _ = Services.ConnectivityService.PublishAsync(
                    ConnectivityIntent.Disconnected(failure, connectId)).ContinueWith(
                    task =>
                    {
                        if (task is { IsFaulted: true, Exception: not null })
                        {
                            Log.Error(task.Exception,
                                "[NETWORK-PROVIDER] Unhandled exception publishing disconnected event");
                        }
                    },
                    TaskScheduler.Default);
            });
        }

        private async Task ProcessStreamItemAsync(SecureEnvelope envelope,
            uint connectId,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem)
        {
            Result<byte[], NetworkFailure> decryptResult =
                _provider.DecryptPayload(connectId, envelope);

            if (decryptResult.IsErr)
            {
                Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Failed to decrypt stream item"));
                return;
            }

            byte[] decryptedData = decryptResult.Unwrap();
            Result<Unit, NetworkFailure> itemResult = await onStreamItem(decryptedData).ConfigureAwait(false);

            if (itemResult.IsErr)
            {
                Result<byte[], NetworkFailure>.Err(itemResult.UnwrapErr());
                return;
            }

            Result<byte[], NetworkFailure>.Ok(decryptedData);
        }

        private void CleanupActiveStream(uint connectId) => ActiveStreams.TryRemove(connectId, out _);

        private void NotifyStreamSuccess(uint connectId)
        {
            bool exitedOutage = Interlocked.CompareExchange(ref OutageState, 0, 1) == 1;

            if (!exitedOutage)
            {
                return;
            }

            lock (OutageLock)
            {
                if (!OutageCompletionSource.Task.IsCompleted)
                {
                    OutageCompletionSource.TrySetResult(true);
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                _ = Services.ConnectivityService.PublishAsync(
                    ConnectivityIntent.Connected(connectId)).ContinueWith(
                    task =>
                    {
                        if (task is { IsFaulted: true, Exception: not null })
                        {
                            Log.Error(task.Exception,
                                "[NETWORK-PROVIDER] Unhandled exception publishing connected event");
                        }
                    },
                    TaskScheduler.Default);
            });
        }

        private async Task<Result<Unit, NetworkFailure>> ProcessStreamDirectly(
            ServiceRequest request,
            Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
            uint connectId,
            CancellationToken token)
        {
            Result<RpcFlow, NetworkFailure> invokeResult =
                await Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

            if (invokeResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
            }

            RpcFlow flow = invokeResult.Unwrap();
            if (flow is not RpcFlow.InboundStream inboundStream)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(
                        $"Expected InboundStream flow but received {flow.GetType().Name}"));
            }

            await foreach (Result<SecureEnvelope, NetworkFailure> streamItem in
                           inboundStream.Stream.WithCancellation(token))
            {
                if (streamItem.IsErr)
                {
                    continue;
                }

                SecureEnvelope streamPayload = streamItem.Unwrap();
                Result<byte[], NetworkFailure> streamDecryptedData =
                    _provider.DecryptPayload(connectId, streamPayload);
                if (streamDecryptedData.IsErr)
                {
                    continue;
                }

                await onStreamItem(streamDecryptedData.Unwrap()).ConfigureAwait(false);
            }

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        private async Task<Result<Unit, NetworkFailure>> SendSendStreamRequestAsync(
            uint logicalOperationId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            ServiceFlowType flowType,
            RpcRequestContext requestContext,
            uint connectId,
            CancellationToken token)
        {
            Result<ServiceRequest, NetworkFailure> serviceRequestResult = _provider.BuildRequestWithId(
                connectId, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

            if (serviceRequestResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
            }

            ServiceRequest request = serviceRequestResult.Unwrap();

            Result<RpcFlow, NetworkFailure> invokeResult =
                await Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

            if (invokeResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
            }

            RpcFlow flow = invokeResult.Unwrap();
            return Result<Unit, NetworkFailure>.Err(flow is not RpcFlow.OutboundSink
                ? NetworkFailure.InvalidRequestType($"Expected OutboundSink flow but received {flow.GetType().Name}")
                : NetworkFailure.InvalidRequestType("Client streaming is not yet implemented"));
        }

        private async Task<Result<Unit, NetworkFailure>> SendBidirectionalStreamRequestAsync(
            uint logicalOperationId,
            RpcServiceType serviceType,
            byte[] plainBuffer,
            ServiceFlowType flowType,
            RpcRequestContext requestContext,
            uint connectId,
            CancellationToken token)
        {
            Result<ServiceRequest, NetworkFailure> serviceRequestResult = _provider.BuildRequestWithId(
                connectId, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

            if (serviceRequestResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
            }

            ServiceRequest request = serviceRequestResult.Unwrap();

            Result<RpcFlow, NetworkFailure> invokeResult =
                await Dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

            if (invokeResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
            }

            RpcFlow flow = invokeResult.Unwrap();
            if (flow is not RpcFlow.BidirectionalStream)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(
                        $"Expected BidirectionalStream flow but received {flow.GetType().Name}"));
            }

            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Bidirectional streaming is not yet implemented"));
        }

        private async Task WaitForOutageRecoveryAsync(CancellationToken token, bool waitForRecovery = true)
        {
            if (Volatile.Read(ref OutageState) == 0)
            {
                return;
            }

            if (!waitForRecovery)
            {
                return;
            }

            Task waitTask;
            lock (OutageLock)
            {
                waitTask = OutageCompletionSource.Task;
            }

            using CancellationTokenSource cts =
                CancellationTokenSource.CreateLinkedTokenSource(token, ShutdownCancellationToken.Token);
            cts.CancelAfter(NetworkConstants.Timeouts.OutageRecoveryTimeout);

            try
            {
                await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ShutdownCancellationToken.Token.IsCancellationRequested)
            {
                throw new ObjectDisposedException(nameof(NetworkProvider), "Provider is shutting down");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    "Outage recovery timeout expired - secrecy channel not restored within timeout period");
            }
        }

        private static bool CanServiceTypeBeDuplicated(RpcServiceType serviceType)
        {
            return serviceType switch
            {
                RpcServiceType.InitiateVerification => true,
                RpcServiceType.ValidateMobileNumber => true,
                _ => false
            };
        }
    }
}
