using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Configuration;
using Ecliptix.Protocol.System.Connection;
using Ecliptix.Protocol.System.Identity;
using Ecliptix.Protocol.System.Interfaces;
using Ecliptix.Protocol.System.Models.Bundles;
using Ecliptix.Protocol.System.Protocol;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed class NetworkProvider(
    NetworkProviderDependencies dependencies,
    NetworkProviderServices services,
    NetworkProviderSecurity security)
    : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private static TaskCompletionSource<bool> CreateOutageTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeStreams = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingRequests = new();
    private readonly Lock _cancellationLock = new();
    private readonly CancellationTokenSource _shutdownCancellationToken = new();
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _channelGates = new();
    private readonly SemaphoreSlim _retryPendingRequestsGate = new(1, 1);
    private readonly Lock _outageLock = new();
    private readonly Lock _disposeLock = new();
    private readonly Lock _appInstanceSetterLock = new();

    private CancellationTokenSource? _connectionRecoveryCts;
    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;
    private int _outageState;
    private TaskCompletionSource<bool> _outageCompletionSource = CreateOutageTcs();
    private volatile bool _disposed;

    public ApplicationInstanceSettings ApplicationInstanceSettings
    {
        get
        {
            if (!_applicationInstanceSettings.IsSome)
            {
                throw new InvalidOperationException(
                    "ApplicationInstanceSettings has not been initialized. Call InitiateEcliptixProtocolSystem first.");
            }

            return _applicationInstanceSettings.Value!;
        }
    }

    private async Task<Result<(SecureEnvelope Envelope, CertificatePinningService Service), NetworkFailure>>
        PrepareSecrecyChannelEnvelopeAsync(uint connectId, PubKeyExchangeType exchangeType,
            EcliptixProtocolSystem protocolSystem)
    {
        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, exchangeType);

        if (pubKeyExchangeRequest.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                pubKeyExchangeRequest.UnwrapErr().ToNetworkFailure());
        }

        Option<CertificatePinningService> certificatePinningService =
            await security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

        if (!certificatePinningService.IsSome)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        byte[] originalData = pubKeyExchangeRequest.Unwrap().ToByteArray();

        Result<byte[], NetworkFailure> encryptResult =
            security.RsaChunkEncryptor.EncryptInChunks(certificatePinningService.Value!, originalData);
        if (encryptResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        byte[] combinedEncryptedPayload = encryptResult.Unwrap();

        EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
            requestId: connectId,
            nonce: ByteString.Empty,
            ratchetIndex: 0,
            envelopeType: EnvelopeType.Request
        );

        SecureEnvelope envelope = EnvelopeBuilder.CreateSecureEnvelope(
            metadata,
            ByteString.CopyFrom(combinedEncryptedPayload)
        );

        return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Ok((envelope,
            certificatePinningService.Value!));
    }

    private Result<PubKeyExchange, NetworkFailure> ProcessSecrecyChannelResponse(
        SecureEnvelope responseEnvelope, CertificatePinningService certificatePinningService,
        PubKeyExchangeType exchangeType, EcliptixProtocolSystem protocolSystem)
    {
        if (exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            CertificatePinningBoolResult certificatePinningBoolResult =
                certificatePinningService.VerifyServerSignature(
                    responseEnvelope.EncryptedPayload.Memory,
                    responseEnvelope.AuthenticationTag.Memory);

            if (!certificatePinningBoolResult.IsSuccess)
            {
                return Result<PubKeyExchange, NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption(
                        $"Server signature verification failed: {certificatePinningBoolResult.ERROR?.Message}"));
            }
        }

        byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();

        Result<byte[], NetworkFailure> decryptResult =
            security.RsaChunkEncryptor.DecryptInChunks(certificatePinningService, combinedEncryptedResponse);
        if (decryptResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(decryptResult.UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

        Result<Unit, EcliptixProtocolFailure> completeResult =
            protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);
        return completeResult.IsErr
            ? Result<PubKeyExchange, NetworkFailure>.Err(completeResult.UnwrapErr().ToNetworkFailure())
            : Result<PubKeyExchange, NetworkFailure>.Ok(peerPubKeyExchange);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelInternalAsync(
        SecrecyChannelRequest request)
    {
        PublishConnectingEventIfNeeded(request.ExchangeType, request.ConnectId);

        Result<EcliptixProtocolSystem, NetworkFailure> protocolSystemResult = GetProtocolSystem(request.ConnectId);
        if (protocolSystemResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(protocolSystemResult.UnwrapErr());
        }

        EcliptixProtocolSystem protocolSystem = protocolSystemResult.Unwrap();

        Result<(SecureEnvelope Envelope, CertificatePinningService Service), NetworkFailure> prepareResult =
            await PrepareSecrecyChannelEnvelopeAsync(request.ConnectId, request.ExchangeType, protocolSystem);

        if (prepareResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(prepareResult.UnwrapErr());
        }

        (SecureEnvelope envelope, CertificatePinningService certificatePinningService) = prepareResult.Unwrap();

        Result<SecureEnvelope, NetworkFailure> establishResult =
            await ExecuteEstablishChannelRpcAsync(request, envelope);

        if (establishResult.IsErr)
        {
            return HandleEstablishChannelFailure(establishResult.UnwrapErr(), request);
        }

        Result<PubKeyExchange, NetworkFailure> processResult =
            ProcessSecrecyChannelResponse(establishResult.Unwrap(), certificatePinningService, request.ExchangeType,
                protocolSystem);

        if (processResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(processResult.UnwrapErr());
        }

        return await CreateAndPersistSessionStateAsync(
            request,
            processResult.Unwrap(),
            protocolSystem);
    }

    private void PublishConnectingEventIfNeeded(PubKeyExchangeType exchangeType, uint connectId)
    {
        if (exchangeType != PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            services.ConnectivityService.PublishAsync(ConnectivityIntent.Connecting(connectId)).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing connecting event");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private Result<EcliptixProtocolSystem, NetworkFailure> GetProtocolSystem(uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<EcliptixProtocolSystem, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        return Result<EcliptixProtocolSystem, NetworkFailure>.Ok(protocolSystem);
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteEstablishChannelRpcAsync(
        SecrecyChannelRequest request,
        SecureEnvelope envelope)
    {
        CancellationToken finalToken = request.CancellationToken == CancellationToken.None
            ? GetConnectionRecoveryToken()
            : request.CancellationToken;

        const string operationName = "EstablishSecrecyChannel";

        if (request.MaxRetries.HasValue)
        {
            return await services.RetryStrategy.ExecuteRpcOperationAsync(
                (_, ct) => dependencies.RpcServiceManager.EstablishSecrecyChannelAsync(
                    services.ConnectivityService,
                    envelope,
                    request.ExchangeType,
                    cancellationToken: ct),
                operationName,
                request.ConnectId,
                serviceType: RpcServiceType.EstablishSecrecyChannel,
                maxRetries: request.MaxRetries.Value,
                cancellationToken: finalToken).ConfigureAwait(false);
        }

        return await services.RetryStrategy.ExecuteRpcOperationAsync(
            (_, ct) => dependencies.RpcServiceManager.EstablishSecrecyChannelAsync(
                services.ConnectivityService,
                envelope,
                request.ExchangeType,
                cancellationToken: ct),
            operationName,
            request.ConnectId,
            serviceType: RpcServiceType.EstablishSecrecyChannel,
            cancellationToken: finalToken).ConfigureAwait(false);
    }

    private Result<Option<EcliptixSessionState>, NetworkFailure> HandleEstablishChannelFailure(
        NetworkFailure failure,
        SecrecyChannelRequest request)
    {
        if (request.EnablePendingRegistration && ShouldQueueSecrecyChannelRetry(failure))
        {
            QueueSecrecyChannelEstablishRetry(request.ConnectId, request.ExchangeType, request.MaxRetries,
                request.SaveState);
        }

        return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(failure);
    }

    private Task<Result<Option<EcliptixSessionState>, NetworkFailure>> CreateAndPersistSessionStateAsync(
        SecrecyChannelRequest request,
        PubKeyExchange peerPubKeyExchange,
        EcliptixProtocolSystem protocolSystem)
    {
        if (!ShouldPersistSessionState(request))
        {
            return Task.FromResult(
                Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None));
        }

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            CreateSessionState(request.ConnectId, peerPubKeyExchange, protocolSystem);

        if (stateResult.IsErr)
        {
            return Task.FromResult(
                Result<Option<EcliptixSessionState>, NetworkFailure>.Err(stateResult.UnwrapErr()));
        }

        if (!request.EnablePendingRegistration)
        {
            return Task.FromResult(
                Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(
                    Option<EcliptixSessionState>.Some(stateResult.Unwrap())));
        }

        services.PendingRequestManager.RemovePendingRequest(
            BuildSecrecyChannelPendingKey(request.ConnectId, request.ExchangeType));
        ExitOutage();

        return Task.FromResult(
            Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(
                Option<EcliptixSessionState>.Some(stateResult.Unwrap())));
    }

    private static bool ShouldPersistSessionState(SecrecyChannelRequest request) =>
        request is { SaveState: true, ExchangeType: PubKeyExchangeType.DataCenterEphemeralConnect };

    private static Result<EcliptixSessionState, NetworkFailure> CreateSessionState(
        uint connectId,
        PubKeyExchange peerPubKeyExchange,
        EcliptixProtocolSystem protocolSystem)
    {
        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();

        if (connection == null)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                new NetworkFailure(
                    NetworkFailureType.DATA_CENTER_NOT_RESPONDING,
                    "Connection has not been established yet."));
        }

        Result<EcliptixSessionState, EcliptixProtocolFailure> stateResult =
            idKeys.ToProtoState()
                .AndThen(identityKeysProto => connection.ToProtoState()
                    .Map(ratchetStateProto => new EcliptixSessionState
                    {
                        ConnectId = connectId,
                        IdentityKeys = identityKeysProto,
                        PeerHandshakeMessage = peerPubKeyExchange,
                        RatchetState = ratchetStateProto
                    })
                );

        return stateResult.ToNetworkFailure();
    }

    public void SetCountry(string country)
    {
        lock (_appInstanceSetterLock)
        {
            if (!_applicationInstanceSettings.IsSome)
            {
                return;
            }

            ApplicationInstanceSettings current = _applicationInstanceSettings.Value!;
            ApplicationInstanceSettings updated = current.Clone();
            updated.Country = country;
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(updated);
        }
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys =
            EcliptixSystemIdentityKeys.Create(NetworkConstants.Protocol.DEFAULT_ONE_TIME_KEY_COUNT).Unwrap();

        DetermineExchangeTypeFromConnectId(applicationInstanceSettings, connectId);

        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        protocolSystem.SetEventHandler(this);

        _connections.TryAdd(connectId, protocolSystem);

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);
        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? AppCultureSettingsConstants.DEFAULT_CULTURE_CODE
            : applicationInstanceSettings.Culture;

        dependencies.RpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? system))
        {
            return;
        }

        system.Dispose();
    }

    public void ClearExhaustedOperations() => services.RetryStrategy.ClearExhaustedOperations();

    public bool HasConnection(uint connectId) => _connections.ContainsKey(connectId);

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

    public async Task<Result<Unit, NetworkFailure>> ExecuteReceiveStreamRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        bool allowDuplicates = false,
        CancellationToken token = default)
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
        RetryBehavior retryBehavior = security.RetryPolicyProvider.GetRetryBehavior(request.ServiceType);

        string requestKey = GenerateRequestKey(request.ConnectId, request.ServiceType, request.PlainBuffer);
        bool shouldAllowDuplicates = request.AllowDuplicateRequests || CanServiceTypeBeDuplicated(request.ServiceType);

        Result<Unit, NetworkFailure>? duplicateCheckResult =
            TryRegisterRequest(requestKey, shouldAllowDuplicates, out CancellationTokenSource requestCts);
        if (duplicateCheckResult.HasValue)
        {
            return duplicateCheckResult.Value;
        }

        using RequestCancellationContext cancellationContext =
            new(request.CancellationToken, requestCts, shouldAllowDuplicates, requestKey, _pendingRequests);

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

        if (_pendingRequests.TryAdd(requestKey, cancellationTokenSource))
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

        if (!_connections.TryGetValue(requestContext.ConnectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return HandleMissingConnection();
        }

        uint logicalOperationId = GenerateLogicalOperationId(
            requestContext.ConnectId, requestContext.ServiceType, requestContext.PlainBuffer);

        Result<Unit, NetworkFailure> networkResult = await ExecuteServiceFlowAsync(
                protocolSystem, logicalOperationId, requestContext, operationToken)
            .ConfigureAwait(false);

        if (networkResult.IsOk && Volatile.Read(ref _outageState) == 1)
        {
            ExitOutage();
        }

        return networkResult;
    }

    private Result<Unit, NetworkFailure> HandleMissingConnection()
    {
        NetworkFailure noConnectionFailure = NetworkFailure.DataCenterNotResponding(
            "Connection unavailable - server may be recovering");

        _ = services.ConnectivityService.PublishAsync(
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
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        ServiceRequestContext requestContext,
        CancellationToken operationToken)
    {
        return requestContext.FlowType switch
        {
            ServiceFlowType.SINGLE => await SendUnaryRequestAsync(
                    protocolSystem, logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                    requestContext.FlowType, requestContext.OnCompleted, requestContext.ConnectId,
                    requestContext.RetryBehavior, operationToken)
                .ConfigureAwait(false),
            ServiceFlowType.RECEIVE_STREAM => await SendReceiveStreamRequestAsync(
                    protocolSystem, logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                    requestContext.FlowType, requestContext.RequestContext, requestContext.OnCompleted,
                    requestContext.RetryBehavior, requestContext.ConnectId, operationToken)
                .ConfigureAwait(false),
            ServiceFlowType.SEND_STREAM => await SendSendStreamRequestAsync(
                    protocolSystem, logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                    requestContext.FlowType, requestContext.RequestContext, operationToken)
                .ConfigureAwait(false),
            ServiceFlowType.BIDIRECTIONAL_STREAM => await SendBidirectionalStreamRequestAsync(
                    protocolSystem, logicalOperationId, requestContext.ServiceType, requestContext.PlainBuffer,
                    requestContext.FlowType, requestContext.RequestContext, operationToken)
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
                        // Expected: CTS may already be disposed of during cleanup
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

    private async Task RetryPendingRequestsAfterRecovery()
    {
        bool acquired = false;
        try
        {
            await _retryPendingRequestsGate.WaitAsync(_shutdownCancellationToken.Token).ConfigureAwait(false);
            acquired = true;
            await services.PendingRequestManager.RetryAllPendingRequestsAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (acquired)
            {
                _retryPendingRequestsGate.Release();
            }
        }
    }

    public enum RestoreRetryMode
    {
        AUTO_RETRY,
        MANUAL_RETRY,
        DIRECT_NO_RETRY
    }

    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelAsync(
        EcliptixSessionState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings,
        RestoreRetryMode retryMode = RestoreRetryMode.AUTO_RETRY,
        bool enablePendingRegistration = true,
        CancellationToken cancellationToken = default)
    {
        InitializeApplicationSettings(applicationInstanceSettings);
        SetupRpcMetadata(applicationInstanceSettings);

        RestoreChannelRequest request = new();
        Result<RestoreChannelResponse, NetworkFailure> restoreResponse =
            await ExecuteRestoreChannelByRetryModeAsync(
                request,
                ecliptixSecrecyChannelState.ConnectId,
                retryMode,
                cancellationToken);

        if (restoreResponse.IsErr)
        {
            return HandleRestoreFailure(
                restoreResponse.UnwrapErr(),
                ecliptixSecrecyChannelState,
                applicationInstanceSettings,
                retryMode,
                enablePendingRegistration);
        }

        return await ProcessRestoreResponseAsync(
            restoreResponse.Unwrap(),
            ecliptixSecrecyChannelState,
            enablePendingRegistration);
    }

    private void InitializeApplicationSettings(ApplicationInstanceSettings settings)
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(settings);
        }
    }

    private void SetupRpcMetadata(ApplicationInstanceSettings settings)
    {
        string? culture = string.IsNullOrEmpty(settings.Culture)
            ? AppCultureSettingsConstants.DEFAULT_CULTURE_CODE
            : settings.Culture;

        dependencies.RpcMetaDataProvider.SetAppInfo(
            Helpers.FromByteStringToGuid(settings.AppInstanceId),
            Helpers.FromByteStringToGuid(settings.DeviceId),
            culture);
    }

    private async Task<Result<RestoreChannelResponse, NetworkFailure>> ExecuteRestoreChannelByRetryModeAsync(
        RestoreChannelRequest request,
        uint connectId,
        RestoreRetryMode retryMode,
        CancellationToken cancellationToken)
    {
        return retryMode switch
        {
            RestoreRetryMode.AUTO_RETRY =>
                await ExecuteWithAutoRetryAsync(request, connectId, cancellationToken),
            RestoreRetryMode.MANUAL_RETRY =>
                await ExecuteWithManualRetryAsync(request, connectId, cancellationToken),
            RestoreRetryMode.DIRECT_NO_RETRY =>
                await ExecuteDirectRestoreAsync(request, cancellationToken),
            _ => Result<RestoreChannelResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Unknown retry mode: {retryMode}"))
        };
    }

    private async Task<Result<RestoreChannelResponse, NetworkFailure>> ExecuteWithAutoRetryAsync(
        RestoreChannelRequest request,
        uint connectId,
        CancellationToken cancellationToken)
    {
        BeginSecrecyChannelEstablishRecovery();
        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CreateLinkedTokenSource(recoveryToken, cancellationToken);

        return await services.RetryStrategy.ExecuteRpcOperationAsync(
            (_, ct) => dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                services.ConnectivityService,
                request,
                cancellationToken: ct),
            "RestoreSecrecyChannel",
            connectId,
            serviceType: RpcServiceType.RestoreSecrecyChannel,
            cancellationToken: combinedCts.Token).ConfigureAwait(false);
    }

    private async Task<Result<RestoreChannelResponse, NetworkFailure>> ExecuteWithManualRetryAsync(
        RestoreChannelRequest request,
        uint connectId,
        CancellationToken cancellationToken)
    {
        BeginSecrecyChannelEstablishRecovery();
        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CreateLinkedTokenSource(recoveryToken, cancellationToken);

        return await services.RetryStrategy.ExecuteManualRetryRpcOperationAsync(
            (_, ct) => dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                services.ConnectivityService,
                request,
                cancellationToken: ct),
            "RestoreSecrecyChannel",
            connectId,
            cancellationToken: combinedCts.Token).ConfigureAwait(false);
    }

    private async Task<Result<RestoreChannelResponse, NetworkFailure>> ExecuteDirectRestoreAsync(
        RestoreChannelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                services.ConnectivityService,
                request,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result<RestoreChannelResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }

    private static CancellationTokenSource CreateLinkedTokenSource(
        CancellationToken token1,
        CancellationToken token2)
    {
        return token2.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(token1, token2)
            : CancellationTokenSource.CreateLinkedTokenSource(token1);
    }

    private Result<bool, NetworkFailure> HandleRestoreFailure(
        NetworkFailure failure,
        EcliptixSessionState sessionState,
        ApplicationInstanceSettings settings,
        RestoreRetryMode retryMode,
        bool enablePendingRegistration)
    {
        if (enablePendingRegistration && ShouldQueueSecrecyChannelRetry(failure))
        {
            QueueSecrecyChannelRestoreRetry(sessionState, settings, retryMode);
        }

        return Result<bool, NetworkFailure>.Err(failure);
    }

    private async Task<Result<bool, NetworkFailure>> ProcessRestoreResponseAsync(
        RestoreChannelResponse response,
        EcliptixSessionState sessionState,
        bool enablePendingRegistration)
    {
        return response.Status switch
        {
            RestoreChannelResponse.Types.Status.SessionRestored =>
                HandleSessionRestored(response, sessionState, enablePendingRegistration),
            RestoreChannelResponse.Types.Status.SessionNotFound =>
                await HandleSessionNotFoundAsync(sessionState.ConnectId),
            _ => Result<bool, NetworkFailure>.Ok(false)
        };
    }

    private Result<bool, NetworkFailure> HandleSessionRestored(
        RestoreChannelResponse response,
        EcliptixSessionState sessionState,
        bool enablePendingRegistration)
    {
        Result<Unit, EcliptixProtocolFailure> syncResult =
            SyncSecrecyChannel(sessionState, response);

        if (syncResult.IsErr)
        {
            EcliptixProtocolFailure error = syncResult.UnwrapErr();
            return error.Message.Contains("Session validation failed")
                ? Result<bool, NetworkFailure>.Ok(false)
                : Result<bool, NetworkFailure>.Err(error.ToNetworkFailure());
        }

        if (enablePendingRegistration)
        {
            services.PendingRequestManager.RemovePendingRequest(
                BuildSecrecyChannelRestoreKey(sessionState.ConnectId));
        }

        ExitOutage();
        return Result<bool, NetworkFailure>.Ok(true);
    }

    private async Task<Result<bool, NetworkFailure>> HandleSessionNotFoundAsync(uint connectId)
    {
        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            Log.Warning(
                "[NETWORK-PROVIDER] Failed to establish secrecy channel after SESSION_NOT_FOUND: {Error}",
                establishResult.UnwrapErr().Message);
        }

        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSessionState, NetworkFailure>> EstablishSecrecyChannelAsync(
        uint connectId)
    {
        SecrecyChannelRequest request = new(
            ConnectId: connectId,
            ExchangeType: PubKeyExchangeType.DataCenterEphemeralConnect,
            MaxRetries: null,
            SaveState: true,
            EnablePendingRegistration: true,
            CancellationToken: CancellationToken.None);

        Result<Option<EcliptixSessionState>, NetworkFailure> result =
            await EstablishSecrecyChannelInternalAsync(request).ConfigureAwait(false);

        if (result.IsErr)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(result.UnwrapErr());
        }

        Option<EcliptixSessionState> stateOption = result.Unwrap();
        if (!stateOption.IsSome)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Failed to create session state"));
        }

        return Result<EcliptixSessionState, NetworkFailure>.Ok(stateOption.Value!);
    }

    public async Task<Result<uint, NetworkFailure>> EnsureProtocolForTypeAsync(
        PubKeyExchangeType exchangeType)
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application not initialized"));
        }

        ApplicationInstanceSettings appSettings = _applicationInstanceSettings.Value!;
        uint connectId = ComputeUniqueConnectId(appSettings, exchangeType);

        if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? existingConnection))
        {
            existingConnection.Dispose();
        }

        InitiateEcliptixProtocolSystemForType(connectId);

        Result<Option<EcliptixSessionState>, NetworkFailure> establishOptionResult =
            await EstablishSecrecyChannelForTypeAsync(connectId, exchangeType).ConfigureAwait(false);

        if (!establishOptionResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Ok(connectId);
        }

        _connections.TryRemove(connectId, out _);
        return Result<uint, NetworkFailure>.Err(establishOptionResult.UnwrapErr());
    }

    private void CancelOperationsForConnection(uint connectId)
    {
        string connectIdPrefix = $"{connectId}_";

        foreach (KeyValuePair<string, CancellationTokenSource> kvp in _pendingRequests)
        {
            if (!kvp.Key.StartsWith(connectIdPrefix))
            {
                continue;
            }

            if (!_pendingRequests.TryRemove(kvp.Key, out CancellationTokenSource? operationCts))
            {
                continue;
            }

            try
            {
                operationCts.Cancel();
                operationCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Intentionally suppressed: CancellationTokenSource already disposed during connection cleanup
            }
        }
    }

    public async Task<Result<Unit, NetworkFailure>> CleanupStreamProtocolAsync(uint connectId)
    {
        CancelOperationsForConnection(connectId);

        if (_activeStreams.TryRemove(connectId, out CancellationTokenSource? cancellationTokenSource))
        {
            await cancellationTokenSource.CancelAsync().ConfigureAwait(false);
            cancellationTokenSource.Dispose();
        }

        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        protocolSystem.Dispose();

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private static RatchetConfig GetRatchetConfigForExchangeType(PubKeyExchangeType exchangeType)
    {
        RatchetConfig config = exchangeType switch
        {
            PubKeyExchangeType.ServerStreaming => RatchetConfig.Create(
                dhRatchetEveryNMessages: 20,
                maxMessagesWithoutRatchet: 100),
            _ => RatchetConfig.Default
        };

        return config;
    }

    private static PubKeyExchangeType DetermineExchangeTypeFromConnectId(
        ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        PubKeyExchangeType[] knownTypes =
        [
            PubKeyExchangeType.DataCenterEphemeralConnect,
            PubKeyExchangeType.ServerStreaming
        ];

        foreach (PubKeyExchangeType exchangeType in knownTypes)
        {
            uint computedConnectId = ComputeUniqueConnectId(applicationInstanceSettings, exchangeType);
            if (computedConnectId != connectId)
            {
                continue;
            }

            return exchangeType;
        }

        return PubKeyExchangeType.DataCenterEphemeralConnect;
    }

    private void InitiateEcliptixProtocolSystemForType(uint connectId
    )
    {
        EcliptixSystemIdentityKeys identityKeys =
            EcliptixSystemIdentityKeys.Create(NetworkConstants.Protocol.DEFAULT_ONE_TIME_KEY_COUNT).Unwrap();

        EcliptixProtocolSystem protocolSystem = new(identityKeys);
        protocolSystem.SetEventHandler(this);

        _connections.TryAdd(connectId, protocolSystem);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelForTypeAsync(
        uint connectId,
        PubKeyExchangeType exchangeType)
    {
        SecrecyChannelRequest request = new(
            ConnectId: connectId,
            ExchangeType: exchangeType,
            MaxRetries: 15,
            SaveState: exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect,
            EnablePendingRegistration: true,
            CancellationToken: CancellationToken.None);

        return await EstablishSecrecyChannelInternalAsync(request).ConfigureAwait(false);
    }

    private Result<Unit, EcliptixProtocolFailure> SyncSecrecyChannel(
        EcliptixSessionState currentState,
        RestoreChannelResponse peerSecrecyChannelState)
    {
        Result<EcliptixProtocolSystem, EcliptixProtocolFailure> systemResult = RecreateSystemFromState(currentState);
        if (systemResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(systemResult.UnwrapErr());
        }

        EcliptixProtocolSystem system = systemResult.Unwrap();

        system.SetEventHandler(this);

        EcliptixProtocolConnection? connection = system.GetConnection();
        if (connection == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not established"));
        }

        Result<Unit, EcliptixProtocolFailure> syncResult = connection.SyncWithRemoteState(
            peerSecrecyChannelState.SendingChainLength,
            peerSecrecyChannelState.ReceivingChainLength
        );

        if (syncResult.IsErr)
        {
            system.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(syncResult.UnwrapErr());
        }

        _ = connection.ToProtoState();

        _connections.TryAdd(currentState.ConnectId, system);
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<EcliptixProtocolSystem, EcliptixProtocolFailure> RecreateSystemFromState(
        EcliptixSessionState state)
    {
        Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> idKeysResult =
            EcliptixSystemIdentityKeys.FromProtoState(state.IdentityKeys);
        if (idKeysResult.IsErr)
        {
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());
        }

        PubKeyExchangeType exchangeType = _applicationInstanceSettings.IsSome
            ? DetermineExchangeTypeFromConnectId(_applicationInstanceSettings.Value!, state.ConnectId)
            : PubKeyExchangeType.DataCenterEphemeralConnect;

        RatchetConfig config = GetRatchetConfigForExchangeType(exchangeType);

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> connResult =
            EcliptixProtocolConnection.FromProtoState(state.ConnectId, state.RatchetState, config, exchangeType);

        if (connResult.IsErr)
        {
            idKeysResult.Unwrap().Dispose();
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
        }

        return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap());
    }

    private static uint GenerateLogicalOperationId(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
    {
        Span<byte> hashBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];

        switch (serviceType.ToString())
        {
            case "OpaqueSignInInitRequest" or "OpaqueSignInFinalizeRequest":
            {
                Span<byte> semanticBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                int written = System.Text.Encoding.UTF8.GetBytes($"auth:signin:{connectId}", semanticBuffer);
                SHA256.HashData(semanticBuffer[..written], hashBuffer);
                break;
            }
            case "OpaqueSignUpInitRequest" or "OpaqueSignUpFinalizeRequest":
            {
                Span<byte> semanticBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                int written = System.Text.Encoding.UTF8.GetBytes($"auth:signup:{connectId}", semanticBuffer);
                SHA256.HashData(semanticBuffer[..written], hashBuffer);
                break;
            }
            case "InitiateVerification":
            {
                Span<byte> payloadHash = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                SHA256.HashData(plainBuffer, payloadHash);

                string semantic =
                    $"stream:{serviceType}:{connectId}:{DateTime.UtcNow.Ticks}:{Convert.ToHexString(payloadHash)}";
                Span<byte> semanticBuffer = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(semantic)];
                int written = System.Text.Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                SHA256.HashData(semanticBuffer[..written], hashBuffer);
                break;
            }
            default:
            {
                Span<byte> payloadHash = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                SHA256.HashData(plainBuffer, payloadHash);

                string semantic = $"data:{serviceType}:{connectId}:{Convert.ToHexString(payloadHash)}";
                Span<byte> semanticBuffer = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(semantic)];
                int written = System.Text.Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                SHA256.HashData(semanticBuffer[..written], hashBuffer);
                break;
            }
        }

        uint rawId = BitConverter.ToUInt32(hashBuffer[..CryptographicConstants.SHA_256_HASH_SIZE]);
        uint finalId = Math.Max(rawId % (uint.MaxValue - NetworkConstants.Protocol.OPERATION_ID_RESERVED_RANGE),
            NetworkConstants.Protocol.OPERATION_ID_MIN_VALUE);

        return finalId;
    }

    private static Result<SecureEnvelope, NetworkFailure> EncryptPayload(
        EcliptixProtocolSystem protocolSystem,
        byte[] plainBuffer)
    {
        Result<SecureEnvelope, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundEnvelope(plainBuffer);

        if (outboundPayload.IsErr)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                outboundPayload.UnwrapErr().ToNetworkFailure());
        }

        SecureEnvelope cipherPayload = outboundPayload.Unwrap();

        return Result<SecureEnvelope, NetworkFailure>.Ok(cipherPayload);
    }

    private static Result<ServiceRequest, NetworkFailure> BuildRequestWithId(
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        RpcRequestContext requestContext)
    {
        Result<SecureEnvelope, NetworkFailure> encryptResult = EncryptPayload(protocolSystem, plainBuffer);

        if (encryptResult.IsErr)
        {
            return Result<ServiceRequest, NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        SecureEnvelope cipherPayload = encryptResult.Unwrap();

        return Result<ServiceRequest, NetworkFailure>.Ok(
            ServiceRequest.New(logicalOperationId, flowType, serviceType, cipherPayload, [], requestContext));
    }

    private async Task<Result<Unit, NetworkFailure>> SendUnaryRequestAsync(
        EcliptixProtocolSystem protocolSystem,
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
            Result<SecureEnvelope, NetworkFailure> encryptResult =
                EncryptPayload(protocolSystem, plainBuffer);

            if (encryptResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(encryptResult.UnwrapErr());
            }

            SecureEnvelope encryptedPayload = encryptResult.Unwrap();
            string stableIdempotencyKey = Guid.NewGuid().ToString("N");

            invokeResult = await services.RetryStrategy.ExecuteRpcOperationAsync(
                (attempt, ct) =>
                {
                    RpcRequestContext attemptContext =
                        RpcRequestContext.CreateNewWithStableKey(stableIdempotencyKey, attempt);
                    lastRequestContext = attemptContext;

                    ServiceRequest request = ServiceRequest.New(
                        logicalOperationId,
                        flowType,
                        serviceType,
                        encryptedPayload,
                        [],
                        attemptContext);

                    return dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, ct);
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

            Result<ServiceRequest, NetworkFailure> serviceRequestResult = BuildRequestWithId(
                protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType, singleAttemptContext);

            if (serviceRequestResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
            }

            ServiceRequest request = serviceRequestResult.Unwrap();
            invokeResult = await dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token)
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

        Result<byte[], EcliptixProtocolFailure> decryptedData =
            protocolSystem.ProcessInboundEnvelope(inboundPayload);
        if (decryptedData.IsErr)
        {
            Log.Error("[CLIENT-DECRYPT-ERROR] Decryption failed. ERROR: {Error}", decryptedData.UnwrapErr().Message);
            NetworkFailure decryptFailure = decryptedData.UnwrapErr().ToNetworkFailure();
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
            RpcServiceType.RecoverySecretKeyComplete or
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
        EcliptixProtocolSystem protocolSystem,
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
            Result<SecureEnvelope, NetworkFailure> encryptResult =
                EncryptPayload(protocolSystem, plainBuffer);

            if (encryptResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(encryptResult.UnwrapErr());
            }

            SecureEnvelope encryptedPayload = encryptResult.Unwrap();
            string stableIdempotencyKey = requestContext.IdempotencyKey;

            return await services.RetryStrategy.ExecuteRpcOperationAsync(
                async (attempt, ct) =>
                {
                    RpcRequestContext attemptContext =
                        RpcRequestContext.CreateNewWithStableKey(stableIdempotencyKey, attempt);

                    ServiceRequest request = ServiceRequest.New(
                        logicalOperationId,
                        flowType,
                        serviceType,
                        encryptedPayload,
                        [],
                        attemptContext);

                    Result<Unit, NetworkFailure> processResult = await ProcessStreamWithRequest(
                        protocolSystem, request, onStreamItem, connectId, ct).ConfigureAwait(false);

                    return processResult;
                },
                $"StreamRequest_{serviceType}",
                connectId,
                serviceType: serviceType,
                maxRetries: Math.Max(0, retryBehavior.MaxAttempts - 1),
                cancellationToken: token).ConfigureAwait(false);
        }

        Result<ServiceRequest, NetworkFailure> serviceRequestResult = BuildRequestWithId(
            protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

        if (serviceRequestResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
        }

        ServiceRequest request = serviceRequestResult.Unwrap();
        return await ProcessStreamWithRequest(protocolSystem, request, onStreamItem, connectId, token)
            .ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> ProcessStreamWithRequest(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        uint connectId,
        CancellationToken token)
    {
        if (connectId == 0)
        {
            return await ProcessStreamDirectly(protocolSystem, request, onStreamItem, token).ConfigureAwait(false);
        }

        using CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        _activeStreams.TryAdd(connectId, linkedTokenSource);

        Result<RpcFlow, NetworkFailure> invokeResult =
            await dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, linkedTokenSource.Token)
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
                await ProcessStreamItemAsync(streamPayload, protocolSystem, onStreamItem).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (linkedTokenSource.Token.IsCancellationRequested)
        {
            // Suppressed
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
                NetworkFailure.InvalidRequestType($"Expected InboundStream flow but received {flow.GetType().Name}"));
        }

        return Result<RpcFlow.InboundStream, NetworkFailure>.Ok(inboundStream);
    }

    private void NotifyStreamError(NetworkFailure failure, uint connectId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = services.ConnectivityService.PublishAsync(
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

    private static async Task ProcessStreamItemAsync(SecureEnvelope envelope,
        EcliptixProtocolSystem protocolSystem,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem)
    {
        Result<byte[], EcliptixProtocolFailure> decryptResult =
            protocolSystem.ProcessInboundEnvelope(envelope);

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

    private void CleanupActiveStream(uint connectId) => _activeStreams.TryRemove(connectId, out _);

    private void NotifyStreamSuccess(uint connectId)
    {
        bool exitedOutage = Interlocked.CompareExchange(ref _outageState, 0, 1) == 1;

        if (!exitedOutage)
        {
            return;
        }

        lock (_outageLock)
        {
            if (!_outageCompletionSource.Task.IsCompleted)
            {
                _outageCompletionSource.TrySetResult(true);
            }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = services.ConnectivityService.PublishAsync(
                ConnectivityIntent.Connected(connectId)).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing connected event");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private async Task<Result<Unit, NetworkFailure>> ProcessStreamDirectly(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

        if (invokeResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.InboundStream inboundStream)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected InboundStream flow but received {flow.GetType().Name}"));
        }

        await foreach (Result<SecureEnvelope, NetworkFailure> streamItem in
                       inboundStream.Stream.WithCancellation(token))
        {
            if (streamItem.IsErr)
            {
                continue;
            }

            SecureEnvelope streamPayload = streamItem.Unwrap();
            Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                protocolSystem.ProcessInboundEnvelope(streamPayload);
            if (streamDecryptedData.IsErr)
            {
                continue;
            }

            await onStreamItem(streamDecryptedData.Unwrap()).ConfigureAwait(false);
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> SendSendStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        RpcRequestContext requestContext,
        CancellationToken token)
    {
        Result<ServiceRequest, NetworkFailure> serviceRequestResult = BuildRequestWithId(
            protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

        if (serviceRequestResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
        }

        ServiceRequest request = serviceRequestResult.Unwrap();

        Result<RpcFlow, NetworkFailure> invokeResult =
            await dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        RpcRequestContext requestContext,
        CancellationToken token)
    {
        Result<ServiceRequest, NetworkFailure> serviceRequestResult = BuildRequestWithId(
            protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType, requestContext);

        if (serviceRequestResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(serviceRequestResult.UnwrapErr());
        }

        ServiceRequest request = serviceRequestResult.Unwrap();

        Result<RpcFlow, NetworkFailure> invokeResult =
            await dependencies.RpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
        if (Volatile.Read(ref _outageState) == 0)
        {
            return;
        }

        if (!waitForRecovery)
        {
            return;
        }

        Task waitTask;
        lock (_outageLock)
        {
            waitTask = _outageCompletionSource.Task;
        }

        using CancellationTokenSource cts =
            CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCancellationToken.Token);
        cts.CancelAfter(NetworkConstants.Timeouts.OutageRecoveryTimeout);

        try
        {
            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCancellationToken.Token.IsCancellationRequested)
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

    internal void ExitOutage()
    {
        if (Interlocked.Exchange(ref _outageState, 0) == 0)
        {
            return;
        }

        CancelConnectionRecoveryToken();

        lock (_outageLock)
        {
            if (!_outageCompletionSource.Task.IsCompleted)
            {
                _outageCompletionSource.TrySetResult(true);
            }
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            services.ConnectivityService.PublishAsync(
                ConnectivityIntent.Connected()).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing connected event");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private CancellationToken GetConnectionRecoveryToken() => EnsureConnectionRecoveryToken();

    private CancellationToken EnsureConnectionRecoveryToken()
    {
        lock (_cancellationLock)
        {
            if (_connectionRecoveryCts == null || _connectionRecoveryCts.IsCancellationRequested)
            {
                _connectionRecoveryCts?.Dispose();

                if (_shutdownCancellationToken.IsCancellationRequested)
                {
                    return _shutdownCancellationToken.Token;
                }

                _connectionRecoveryCts =
                    CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellationToken.Token);
            }

            return _connectionRecoveryCts.Token;
        }
    }

    internal void BeginSecrecyChannelEstablishRecovery()
    {
        if (_disposed || _shutdownCancellationToken.IsCancellationRequested)
        {
            return;
        }

        bool enteredOutage = Interlocked.CompareExchange(ref _outageState, 1, 0) == 0;
        if (enteredOutage)
        {
            lock (_outageLock)
            {
                if (_outageCompletionSource.Task.IsCompleted)
                {
                    _outageCompletionSource = CreateOutageTcs();
                }
            }

            services.ConnectivityService.PublishAsync(
                ConnectivityIntent.Recovering(
                    NetworkFailure.DataCenterNotResponding("Recovering secrecy channel"))).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing recovering event");
                    }
                },
                TaskScheduler.Default);
        }

        EnsureConnectionRecoveryToken();
    }

    private void CancelConnectionRecoveryToken()
    {
        lock (_cancellationLock)
        {
            if (_connectionRecoveryCts == null)
            {
                return;
            }

            try
            {
                if (!_connectionRecoveryCts.IsCancellationRequested)
                {
                    _connectionRecoveryCts.Cancel();
                }
            }
            catch (ObjectDisposedException)
            {
                // Intentionally suppressed: Recovery cancellation token source already disposed
            }
            finally
            {
                _connectionRecoveryCts.Dispose();
                _connectionRecoveryCts = null;
            }
        }
    }

    private async Task<T> WithChannelGate<T>(uint connectId, Func<Task<T>> action)
    {
        SemaphoreSlim gate = _channelGates.GetOrAdd(connectId, _ => new SemaphoreSlim(1, 1));
        bool acquired = false;
        try
        {
            await gate.WaitAsync(_shutdownCancellationToken.Token).ConfigureAwait(false);
            acquired = true;
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(NetworkProvider), "Provider is shutting down");
        }
        finally
        {
            if (acquired)
            {
                gate.Release();
            }
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

    private void PersistProtocolStateInBackground(uint connectId)
    {
        Task.Run(async () =>
        {
            await TryPersistProtocolStateAsync(connectId).ConfigureAwait(false);
        });
    }

    private async Task TryPersistProtocolStateAsync(uint connectId)
    {
        if (_disposed || _shutdownCancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return;
        }

        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null || connection.ExchangeType == PubKeyExchangeType.ServerStreaming)
        {
            return;
        }

        Option<EcliptixSessionState> sessionStateOption = BuildSessionState(connectId, protocolSystem, connection);
        if (sessionStateOption.IsSome)
        {
            await PersistSessionStateAsync(sessionStateOption.Value!, connectId).ConfigureAwait(false);
        }
    }

    private static Option<EcliptixSessionState> BuildSessionState(uint connectId, EcliptixProtocolSystem protocolSystem,
        EcliptixProtocolConnection connection)
    {
        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
        Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

        if (!idKeysStateResult.IsOk || !ratchetStateResult.IsOk)
        {
            return Option<EcliptixSessionState>.None;
        }

        EcliptixSessionState state = new()
        {
            ConnectId = connectId,
            IdentityKeys = idKeysStateResult.Unwrap(),
            RatchetState = ratchetStateResult.Unwrap()
        };

        return Option<EcliptixSessionState>.Some(state);
    }

    public void OnProtocolStateChanged(uint connectId) =>
        PersistProtocolStateInBackground(connectId);

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings? applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType)
    {
        if (applicationInstanceSettings == null)
        {
            throw new InvalidOperationException("ApplicationInstanceSettings is null. Cannot compute connect ID.");
        }

        if (applicationInstanceSettings.AppInstanceId == null || applicationInstanceSettings.AppInstanceId.IsEmpty)
        {
            throw new InvalidOperationException("AppInstanceId is null or empty. Cannot compute connect ID.");
        }

        if (applicationInstanceSettings.DeviceId == null || applicationInstanceSettings.DeviceId.IsEmpty)
        {
            throw new InvalidOperationException("DeviceId is null or empty. Cannot compute connect ID.");
        }

        Guid appInstanceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);

        string appInstanceIdString = appInstanceGuid.ToString();
        string deviceIdString = deviceGuid.ToString();

        uint connectId = Helpers.ComputeUniqueConnectId(
            appInstanceIdString,
            deviceIdString, pubKeyExchangeType);

        return connectId;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                _shutdownCancellationToken.Cancel();
                CleanupPendingRequests();
                CleanupActiveStreams();
                CancelOutageCompletion();
                DisposeConnections();
                DisposeCancellationTokens();
                DisposeSynchronizationPrimitives();
            }
            catch
            {
                // Suppressed
            }
        }
    }

    private void CleanupPendingRequests()
    {
        foreach (KeyValuePair<string, CancellationTokenSource> kv in _pendingRequests.ToArray())
        {
            if (!_pendingRequests.TryRemove(kv.Key, out CancellationTokenSource? cts))
            {
                continue;
            }

            CancelAndDisposeCancellationTokenSource(cts);
        }
    }

    private void CleanupActiveStreams()
    {
        foreach (KeyValuePair<uint, CancellationTokenSource> kv in _activeStreams.ToArray())
        {
            if (!_activeStreams.TryRemove(kv.Key, out CancellationTokenSource? streamCts))
            {
                continue;
            }

            CancelAndDisposeCancellationTokenSource(streamCts);
        }
    }

    private static void CancelAndDisposeCancellationTokenSource(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Suppressed
        }
    }

    private void CancelOutageCompletion()
    {
        lock (_outageLock)
        {
            _outageCompletionSource.TrySetException(new OperationCanceledException("Provider shutting down"));
        }
    }

    private void DisposeConnections()
    {
        List<KeyValuePair<uint, EcliptixProtocolSystem>> connectionsToDispose = new(_connections);
        _connections.Clear();

        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in connectionsToDispose)
        {
            try
            {
                connection.Value.Dispose();
            }
            catch
            {
                // Suppressed
            }
        }
    }

    private void DisposeCancellationTokens()
    {
        lock (_cancellationLock)
        {
            _connectionRecoveryCts?.Dispose();
            _connectionRecoveryCts = null;
        }

        _shutdownCancellationToken.Dispose();
    }

    private void DisposeSynchronizationPrimitives()
    {
        foreach (SemaphoreSlim gate in _channelGates.Values)
        {
            gate.Dispose();
        }

        _channelGates.Clear();
        _retryPendingRequestsGate.Dispose();
    }

    public async Task<Result<Unit, NetworkFailure>> ForceFreshConnectionAsync()
    {
        services.RetryStrategy.ClearExhaustedOperations();

        Result<Unit, NetworkFailure> immediateResult = await PerformImmediateRecoveryLogic().ConfigureAwait(false);

        if (immediateResult.IsOk)
        {
            return immediateResult;
        }

        Result<Unit, NetworkFailure> result = await PerformAdvancedRecoveryWithManualRetryAsync().ConfigureAwait(false);

        return result;
    }

    private async Task<Result<Unit, NetworkFailure>> PerformAdvancedRecoveryWithManualRetryAsync()
    {
        return await PerformRecoveryWithStateRestorationAsync(
            RestoreRetryMode.MANUAL_RETRY,
            "Manual retry restoration failed").ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformImmediateRecoveryLogic()
    {
        return await PerformRecoveryWithStateRestorationAsync(
            RestoreRetryMode.DIRECT_NO_RETRY,
            NetworkConstants.ErrorMessages.SESSION_NOT_FOUND_ON_SERVER,
            failOnMissingState: true).ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformRecoveryWithStateRestorationAsync(
        RestoreRetryMode retryMode,
        string failureMessage,
        bool failOnMissingState = false)
    {
        Result<(uint connectId, byte[] membershipId), NetworkFailure> prerequisitesResult =
            ValidateRecoveryPrerequisites();
        if (prerequisitesResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(prerequisitesResult.UnwrapErr());
        }

        (uint connectId, byte[] membershipId) = prerequisitesResult.Unwrap();
        _connections.TryRemove(connectId, out _);

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            await LoadAndParseStoredState(connectId, membershipId, failOnMissingState, failureMessage)
                .ConfigureAwait(false);

        if (stateResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(stateResult.UnwrapErr());
        }

        bool restorationSucceeded;
        try
        {
            EcliptixSessionState state = stateResult.Unwrap();
            Result<bool, NetworkFailure> restoreResult =
                await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!, retryMode)
                    .ConfigureAwait(false);

            restorationSucceeded = restoreResult.IsOk && restoreResult.Unwrap();

            if (restorationSucceeded)
            {
                PublishConnectionRestored(connectId);
            }
        }
        catch (Exception ex)
        {
            if (retryMode == RestoreRetryMode.DIRECT_NO_RETRY)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding($"Failed to parse stored state: {ex.Message}"));
            }

            restorationSucceeded = false;
        }

        if (!restorationSucceeded)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(failureMessage));
        }

        ExitOutage();
        ResetRetryStrategyAfterOutage();
        await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private Result<(uint connectId, byte[] membershipId), NetworkFailure> ValidateRecoveryPrerequisites()
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return Result<(uint, byte[]), NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        byte[]? membershipId = GetMembershipIdBytes();
        if (membershipId == null)
        {
            return Result<(uint, byte[]), NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("MembershipId not available for state restoration"));
        }

        return Result<(uint, byte[]), NetworkFailure>.Ok((connectId, membershipId));
    }

    private async Task<Result<EcliptixSessionState, NetworkFailure>> LoadAndParseStoredState(
        uint connectId,
        byte[] membershipId,
        bool failOnMissingState,
        string failureMessage)
    {
        Result<byte[], SecureStorageFailure> stateResult =
            await dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
                .ConfigureAwait(false);

        if (stateResult.IsErr)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(failOnMissingState
                ? NetworkFailure.DataCenterNotResponding("No stored state for immediate recovery")
                : NetworkFailure.DataCenterNotResponding(failureMessage));
        }

        try
        {
            byte[] stateBytes = stateResult.Unwrap();
            EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
            return Result<EcliptixSessionState, NetworkFailure>.Ok(state);
        }
        catch (Exception ex)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Failed to parse stored state: {ex.Message}"));
        }
    }

    private void PublishConnectionRestored(uint connectId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = services.ConnectivityService.PublishAsync(
                ConnectivityIntent.Connected(connectId)).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception,
                            "[NETWORK-PROVIDER] Unhandled exception publishing connected event after restoration");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private void ResetRetryStrategyAfterOutage()
    {
        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in _connections)
        {
            services.RetryStrategy.MarkConnectionHealthy(connection.Key);
        }
    }

    private static string BuildSecrecyChannelPendingKey(uint connectId, PubKeyExchangeType exchangeType) =>
        $"secrecy-channel:{connectId}:{exchangeType}";

    private static string BuildSecrecyChannelRestoreKey(uint connectId) =>
        $"secrecy-channel-restore:{connectId}";

    private static bool ShouldQueueSecrecyChannelRetry(NetworkFailure failure)
    {
        return failure.FailureType is NetworkFailureType.DATA_CENTER_NOT_RESPONDING
            or NetworkFailureType.DATA_CENTER_SHUTDOWN
            or NetworkFailureType.PROTOCOL_STATE_MISMATCH
            or NetworkFailureType.RSA_ENCRYPTION_FAILURE;
    }

    private void QueueSecrecyChannelEstablishRetry(uint connectId, PubKeyExchangeType exchangeType, int? maxRetries,
        bool saveState)
    {
        if (_disposed)
        {
            return;
        }

        BeginSecrecyChannelEstablishRecovery();

        string pendingKey = BuildSecrecyChannelPendingKey(connectId, exchangeType);

        services.PendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
        {
            CancellationToken recoveryToken = GetConnectionRecoveryToken();
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, recoveryToken);

            SecrecyChannelRequest request = new(
                ConnectId: connectId,
                ExchangeType: exchangeType,
                MaxRetries: maxRetries,
                SaveState: saveState,
                EnablePendingRegistration: false,
                CancellationToken: linkedCts.Token);

            await EstablishSecrecyChannelInternalAsync(request).ConfigureAwait(false);
        });
    }

    private void QueueSecrecyChannelRestoreRetry(
        EcliptixSessionState sessionState,
        ApplicationInstanceSettings applicationInstanceSettings,
        RestoreRetryMode retryMode)
    {
        if (_disposed)
        {
            return;
        }

        BeginSecrecyChannelEstablishRecovery();

        string pendingKey = BuildSecrecyChannelRestoreKey(sessionState.ConnectId);

        services.PendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
        {
            CancellationToken recoveryToken = GetConnectionRecoveryToken();
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, recoveryToken);

            await RestoreSecrecyChannelAsync(
                    sessionState,
                    applicationInstanceSettings,
                    retryMode,
                    enablePendingRegistration: false,
                    cancellationToken: linkedCts.Token)
                .ConfigureAwait(false);
        });
    }

    private byte[]? GetMembershipIdBytes()
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return null;
        }

        ApplicationInstanceSettings settings = _applicationInstanceSettings.Value!;

        return settings.Membership?.UniqueIdentifier.ToByteArray();
    }

    private async Task PersistSessionStateAsync(EcliptixSessionState state, uint connectId,
        byte[]? membershipIdOverride = null)
    {
        byte[]? membershipId = membershipIdOverride ?? GetMembershipIdBytes();
        if (membershipId == null)
        {
            return;
        }

        Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                state.ToByteString(),
                span => dependencies.SecureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(),
                    membershipId))
            .ConfigureAwait(false);

        if (saveResult.IsErr)
        {
            Log.Warning("[CLIENT-STATE-PERSIST] Failed to save session state. ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, saveResult.UnwrapErr().Message);
        }

        string timestampKey = $"{connectId}_timestamp";
        await dependencies.ApplicationSecureStorageProvider.StoreAsync(timestampKey,
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary())).ConfigureAwait(false);
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return false;
        }

        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null)
        {
            return false;
        }

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
        return peerBundleResult.IsOk;
    }

    public async Task<Result<bool, NetworkFailure>> TryRestoreConnectionAsync(uint connectId)
    {
        return await WithChannelGate(connectId, async () =>
        {
            try
            {
                byte[]? membershipId = GetMembershipIdBytes();
                if (membershipId == null)
                {
                    if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? oldSystem))
                    {
                        oldSystem.Dispose();
                    }

                    SecrecyChannelRequest request = new(
                        ConnectId: connectId,
                        ExchangeType: PubKeyExchangeType.DataCenterEphemeralConnect,
                        MaxRetries: null,
                        SaveState: false,
                        EnablePendingRegistration: false,
                        CancellationToken: CancellationToken.None);

                    Result<Option<EcliptixSessionState>, NetworkFailure> reEstablishResult =
                        await EstablishSecrecyChannelInternalAsync(request).ConfigureAwait(false);

                    return reEstablishResult.IsOk
                        ? Result<bool, NetworkFailure>.Ok(true)
                        : Result<bool, NetworkFailure>.Ok(false);
                }

                Result<byte[], SecureStorageFailure> stateResult =
                    await dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
                        .ConfigureAwait(false);
                if (stateResult.IsErr)
                {
                    return Result<bool, NetworkFailure>.Ok(false);
                }

                byte[] stateBytes = stateResult.Unwrap();
                EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
                Result<bool, NetworkFailure> restoreResult =
                    await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!).ConfigureAwait(false);

                return restoreResult;
            }
            catch (Exception)
            {
                return Result<bool, NetworkFailure>.Ok(false);
            }
        });
    }

    public async Task<Result<Unit, NetworkFailure>> RecreateProtocolWithMasterKeyAsync(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier,
        uint connectId)
    {
        RetryBehavior retryBehavior =
            security.RetryPolicyProvider.GetRetryBehavior(RpcServiceType.EstablishAuthenticatedSecureChannel);
        Result<Unit, NetworkFailure> networkResult = await services.RetryStrategy.ExecuteRpcOperationAsync(
            async (_, _) => await RecreateProtocolWithMasterKeyAsyncInternal(
                masterKeyHandle,
                membershipIdentifier,
                connectId).ConfigureAwait(false),
            "RecreateProtocolWithMasterKey",
            connectId,
            serviceType: RpcServiceType.EstablishAuthenticatedSecureChannel,
            maxRetries: retryBehavior.MaxAttempts - 1,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        if (networkResult.IsOk && Volatile.Read(ref _outageState) == 1)
        {
            ExitOutage();
        }

        return networkResult;
    }

    private async Task<Result<Unit, NetworkFailure>> RecreateProtocolWithMasterKeyAsyncInternal(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier,
        uint connectId)
    {
        byte[]? masterKeyBytes = null;
        byte[]? rootKeyBytes = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(
                        $"Failed to read master key: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            string membershipId = Helpers.FromByteStringToGuid(membershipIdentifier).ToString();

            rootKeyBytes = new byte[CryptographicConstants.AES_KEY_SIZE];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: masterKeyBytes,
                output: rootKeyBytes,
                salt: null,
                info: "ecliptix-protocol-root-key"u8.ToArray()
            );

            Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> identityKeysResult =
                EcliptixSystemIdentityKeys.CreateFromMasterKey(masterKeyBytes, membershipId,
                    NetworkConstants.Protocol.DEFAULT_ONE_TIME_KEY_COUNT);

            if (identityKeysResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(
                    identityKeysResult.UnwrapErr().ToNetworkFailure());
            }

            EcliptixSystemIdentityKeys identityKeys = identityKeysResult.Unwrap();

            if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? inUseProtocol))
            {
                CancelOperationsForConnection(connectId);
                inUseProtocol.Dispose();
            }

            const PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect;
            EcliptixProtocolSystem? initiatedProtocol = new(identityKeys);

            try
            {
                initiatedProtocol.SetEventHandler(this);

                Result<PubKeyExchange, EcliptixProtocolFailure> peerExchangeResult =
                    initiatedProtocol.BeginDataCenterPubKeyExchange(connectId, exchangeType);

                if (peerExchangeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        peerExchangeResult.UnwrapErr().ToNetworkFailure());
                }

                PubKeyExchange clientExchange = peerExchangeResult.Unwrap();

                AuthenticatedEstablishRequest authenticatedRequest = new()
                {
                    MembershipUniqueId = membershipIdentifier, ClientPubKeyExchange = clientExchange
                };

                Result<SecureEnvelope, NetworkFailure> serverResponseResult =
                    await dependencies.RpcServiceManager.EstablishAuthenticatedSecureChannelAsync(
                        services.ConnectivityService, authenticatedRequest).ConfigureAwait(false);

                if (serverResponseResult.IsErr)
                {
                    NetworkFailure failure = serverResponseResult.UnwrapErr();
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(failure);
                }

                SecureEnvelope responseEnvelope = serverResponseResult.Unwrap();

                Option<CertificatePinningService> certificatePinningService =
                    await security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

                if (!certificatePinningService.IsSome)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
                }

                byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();
                Result<byte[], NetworkFailure> decryptResult =
                    security.RsaChunkEncryptor.DecryptInChunks(certificatePinningService.Value!,
                        combinedEncryptedResponse);

                if (decryptResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(decryptResult.UnwrapErr());
                }

                PubKeyExchange serverExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

                Result<Unit, EcliptixProtocolFailure> completeResult =
                    initiatedProtocol.CompleteAuthenticatedPubKeyExchange(serverExchange, rootKeyBytes);

                if (completeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        completeResult.UnwrapErr().ToNetworkFailure());
                }

                _connections.TryAdd(connectId, initiatedProtocol);

                EcliptixProtocolConnection? connection = initiatedProtocol.GetConnection();
                if (connection != null)
                {
                    Result<EcliptixSessionState, EcliptixProtocolFailure> sessionStateResult =
                        identityKeys.ToProtoState()
                            .AndThen(identityKeysProto => connection.ToProtoState()
                                .Map(ratchetStateProto => new EcliptixSessionState
                                {
                                    ConnectId = connectId,
                                    IdentityKeys = identityKeysProto,
                                    PeerHandshakeMessage = serverExchange,
                                    RatchetState = ratchetStateProto
                                })
                            );

                    if (sessionStateResult.IsOk)
                    {
                        EcliptixSessionState sessionState = sessionStateResult.Unwrap();

                        await PersistSessionStateAsync(sessionState, connectId, membershipIdentifier.ToByteArray())
                            .ConfigureAwait(false);
                    }
                }

                initiatedProtocol = null;
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
            finally
            {
                initiatedProtocol?.Dispose();
            }
        }
        catch (Exception ex)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Failed to recreate protocol: {ex.Message}"));
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }

            if (rootKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(rootKeyBytes);
            }
        }
    }

    private async Task CleanupFailedAuthenticationAsync(uint connectId)
    {
        Result<Unit, SecureStorageFailure> deleteResult =
            await dependencies.SecureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning(
                "[CLIENT-AUTH-CLEANUP] Failed to delete protocol state during authentication cleanup. ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }
    }
}
