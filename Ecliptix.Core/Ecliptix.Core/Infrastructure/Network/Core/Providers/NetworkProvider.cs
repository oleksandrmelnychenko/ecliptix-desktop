using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Crypto;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Core;
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

public sealed record NetworkProviderDependencies(
    IRpcServiceManager RpcServiceManager,
    IApplicationSecureStorageProvider ApplicationSecureStorageProvider,
    ISecureProtocolStateStorage SecureProtocolStateStorage,
    IRpcMetaDataProvider RpcMetaDataProvider);

public sealed record NetworkProviderServices(
    IConnectivityService ConnectivityService,
    IRetryStrategy RetryStrategy,
    IPendingRequestManager PendingRequestManager);

public sealed record NetworkProviderSecurity(
    ICertificatePinningServiceFactory CertificatePinningServiceFactory,
    IRsaChunkEncryptor RsaChunkEncryptor,
    IRetryPolicyProvider RetryPolicyProvider);

public sealed class NetworkProvider : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly IRpcServiceManager rpcServiceManager;
    private readonly IApplicationSecureStorageProvider applicationSecureStorageProvider;
    private readonly ISecureProtocolStateStorage secureProtocolStateStorage;
    private readonly IRpcMetaDataProvider rpcMetaDataProvider;
    private readonly IConnectivityService connectivityService;
    private readonly IRetryStrategy retryStrategy;
    private readonly IPendingRequestManager pendingRequestManager;
    private readonly ICertificatePinningServiceFactory certificatePinningServiceFactory;
    private readonly IRsaChunkEncryptor rsaChunkEncryptor;
    private readonly IRetryPolicyProvider retryPolicyProvider;

    public NetworkProvider(
        NetworkProviderDependencies dependencies,
        NetworkProviderServices services,
        NetworkProviderSecurity security)
    {
        rpcServiceManager = dependencies.RpcServiceManager;
        applicationSecureStorageProvider = dependencies.ApplicationSecureStorageProvider;
        secureProtocolStateStorage = dependencies.SecureProtocolStateStorage;
        rpcMetaDataProvider = dependencies.RpcMetaDataProvider;
        connectivityService = services.ConnectivityService;
        retryStrategy = services.RetryStrategy;
        pendingRequestManager = services.PendingRequestManager;
        certificatePinningServiceFactory = security.CertificatePinningServiceFactory;
        rsaChunkEncryptor = security.RsaChunkEncryptor;
        retryPolicyProvider = security.RetryPolicyProvider;
    }
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

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelInternalAsync(
        uint connectId,
        PubKeyExchangeType EXCHANGE_TYPE,
        int? maxRetries = null,
        bool saveState = true,
        CancellationToken cancellationToken = default,
        bool enablePendingRegistration = true)
    {
        if (EXCHANGE_TYPE == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                connectivityService.PublishAsync(ConnectivityIntent.Connecting(connectId)).ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing connecting event");
                        }
                    },
                    TaskScheduler.Default);
            });
        }

        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, EXCHANGE_TYPE);

        if (pubKeyExchangeRequest.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                pubKeyExchangeRequest.UnwrapErr().ToNetworkFailure());
        }

        EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
            requestId: connectId,
            nonce: ByteString.Empty,
            ratchetIndex: 0,
            envelopeType: EnvelopeType.Request
        );

        Option<CertificatePinningService> certificatePinningService =
            await certificatePinningServiceFactory.GetOrInitializeServiceAsync();

        if (!certificatePinningService.IsSome)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        byte[] originalData = pubKeyExchangeRequest.Unwrap().ToByteArray();

        Result<byte[], NetworkFailure> encryptResult =
            rsaChunkEncryptor.EncryptInChunks(certificatePinningService.Value!, originalData);
        if (encryptResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        byte[] combinedEncryptedPayload = encryptResult.Unwrap();

        SecureEnvelope envelope = EnvelopeBuilder.CreateSecureEnvelope(
            metadata,
            ByteString.CopyFrom(combinedEncryptedPayload)
        );

        CancellationToken finalToken = cancellationToken == CancellationToken.None
            ? GetConnectionRecoveryToken()
            : cancellationToken;

        Result<SecureEnvelope, NetworkFailure> establishAppDeviceSecrecyChannelResult;
        if (maxRetries.HasValue)
        {
            establishAppDeviceSecrecyChannelResult = await retryStrategy.ExecuteRpcOperationAsync(
                (_, ct) => rpcServiceManager.EstablishSecrecyChannelAsync(connectivityService, envelope,
                    EXCHANGE_TYPE,
                    cancellationToken: ct),
                "EstablishSecrecyChannel",
                connectId,
                serviceType: RpcServiceType.EstablishSecrecyChannel,
                maxRetries: maxRetries.Value,
                cancellationToken: finalToken).ConfigureAwait(false);
        }
        else
        {
            establishAppDeviceSecrecyChannelResult = await retryStrategy.ExecuteRpcOperationAsync(
                (_, ct) => rpcServiceManager.EstablishSecrecyChannelAsync(connectivityService, envelope,
                    EXCHANGE_TYPE,
                    cancellationToken: ct),
                "EstablishSecrecyChannel",
                connectId,
                serviceType: RpcServiceType.EstablishSecrecyChannel,
                cancellationToken: finalToken).ConfigureAwait(false);
        }

        if (establishAppDeviceSecrecyChannelResult.IsErr)
        {
            NetworkFailure failure = establishAppDeviceSecrecyChannelResult.UnwrapErr();
            if (enablePendingRegistration && ShouldQueueSecrecyChannelRetry(failure))
            {
                QueueSecrecyChannelEstablishRetry(connectId, EXCHANGE_TYPE, maxRetries, saveState);
            }

            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(failure);
        }

        SecureEnvelope responseEnvelope = establishAppDeviceSecrecyChannelResult.Unwrap();

        if (EXCHANGE_TYPE == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            CertificatePinningBoolResult certificatePinningBoolResult =
                certificatePinningService.Value!.VerifyServerSignature(
                    responseEnvelope.EncryptedPayload.Memory,
                    responseEnvelope.AuthenticationTag.Memory);

            if (!certificatePinningBoolResult.IsSuccess)
            {
                return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption(
                        $"Server signature verification failed: {certificatePinningBoolResult.ERROR?.Message}"));
            }
        }

        byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();

        Result<byte[], NetworkFailure> decryptResult =
            rsaChunkEncryptor.DecryptInChunks(certificatePinningService.Value!, combinedEncryptedResponse);
        if (decryptResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(decryptResult.UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

        Result<Unit, EcliptixProtocolFailure> completeResult =
            protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);
        if (completeResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                completeResult.UnwrapErr().ToNetworkFailure());
        }

        if (!saveState || EXCHANGE_TYPE != PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None);
        }

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                new NetworkFailure(NetworkFailureType.DATA_CENTER_NOT_RESPONDING,
                    "Connection has not been established yet."));
        }

        Result<EcliptixSessionState, EcliptixProtocolFailure> ecliptixSecrecyChannelStateResult =
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

        Result<Option<EcliptixSessionState>, NetworkFailure> mappedResult =
            ecliptixSecrecyChannelStateResult.ToNetworkFailure().Map(Option<EcliptixSessionState>.Some);

        if (enablePendingRegistration && mappedResult.IsOk)
        {
            pendingRequestManager.RemovePendingRequest(BuildSecrecyChannelPendingKey(connectId, EXCHANGE_TYPE));
            ExitOutage();
        }

        return mappedResult;
    }

    public void SetCountry(string country)
    {
        lock (_appInstanceSetterLock)
        {
            if (_applicationInstanceSettings.IsSome)
            {
                ApplicationInstanceSettings current = _applicationInstanceSettings.Value!;
                ApplicationInstanceSettings updated = current.Clone();
                updated.Country = country;
                _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(updated);
            }
        }
    }

    public uint ComputeUniqueConnectId(PubKeyExchangeType pubKeyExchangeType) =>
        ComputeUniqueConnectId(_applicationInstanceSettings.Value!, pubKeyExchangeType);

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

        rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? system))
        {
            return;
        }

        system.Dispose();
    }

    public void ClearExhaustedOperations()
    {
        retryStrategy.ClearExhaustedOperations();
    }

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
        return await ExecuteServiceRequestInternalAsync(
            connectId, serviceType, plainBuffer, ServiceFlowType.Single,
            onCompleted, allowDuplicates, token, waitForRecovery, requestContext).ConfigureAwait(false);
    }

    public async Task<Result<Unit, NetworkFailure>> ExecuteReceiveStreamRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        bool allowDuplicates = false,
        CancellationToken token = default)
    {
        return await ExecuteServiceRequestInternalAsync(
            connectId, serviceType, plainBuffer, ServiceFlowType.ReceiveStream,
            onStreamItem, allowDuplicates, token).ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestInternalAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicateRequests = false,
        CancellationToken cancellationToken = default,
        bool waitForRecovery = true,
        RpcRequestContext? requestContext = null)
    {
        RpcRequestContext effectiveContext = requestContext ?? RpcRequestContext.CreateNew();
        RetryBehavior retryBehavior = retryPolicyProvider.GetRetryBehavior(serviceType);

        string requestKey = GenerateRequestKey(connectId, serviceType, plainBuffer);
        bool shouldAllowDuplicates = allowDuplicateRequests || CanServiceTypeBeDuplicated(serviceType);

        Result<Unit, NetworkFailure>? duplicateCheckResult =
            TryRegisterRequest(requestKey, shouldAllowDuplicates, out CancellationTokenSource requestCts);
        if (duplicateCheckResult.HasValue)
        {
            return duplicateCheckResult.Value;
        }

        using RequestCancellationContext cancellationContext =
            new(cancellationToken, requestCts, shouldAllowDuplicates, requestKey, _pendingRequests);

        try
        {
            return await ExecuteRequestWithProtocolAsync(
                connectId, serviceType, plainBuffer, flowType, onCompleted, effectiveContext,
                retryBehavior, waitForRecovery, cancellationContext.OperationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.OperationCancelled("Request cancelled by caller"));
        }
        catch (OperationCanceledException) when (flowType == ServiceFlowType.ReceiveStream)
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
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        RpcRequestContext effectiveContext,
        RetryBehavior retryBehavior,
        bool waitForRecovery,
        CancellationToken operationToken)
    {
        await WaitForOutageRecoveryAsync(operationToken, waitForRecovery).ConfigureAwait(false);
        operationToken.ThrowIfCancellationRequested();

        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return HandleMissingConnection();
        }

        uint logicalOperationId = GenerateLogicalOperationId(connectId, serviceType, plainBuffer);

        Result<Unit, NetworkFailure> networkResult = await ExecuteServiceFlowAsync(
            protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType,
            onCompleted, effectiveContext, retryBehavior, connectId, operationToken).ConfigureAwait(false);

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

        _ = connectivityService.PublishAsync(
            ConnectivityIntent.ServerShutdown(noConnectionFailure)).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing server shutdown event");
                }
            },
            TaskScheduler.Default);

        return Result<Unit, NetworkFailure>.Err(noConnectionFailure);
    }

    private async Task<Result<Unit, NetworkFailure>> ExecuteServiceFlowAsync(
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        RpcRequestContext effectiveContext,
        RetryBehavior retryBehavior,
        uint connectId,
        CancellationToken operationToken)
    {
        return flowType switch
        {
            ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, logicalOperationId,
                    serviceType, plainBuffer, flowType, onCompleted, connectId, retryBehavior, operationToken)
                .ConfigureAwait(false),
            ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem,
                logicalOperationId,
                serviceType, plainBuffer, flowType, effectiveContext, onCompleted, retryBehavior, connectId,
                operationToken).ConfigureAwait(false),
            ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, logicalOperationId,
                    serviceType, plainBuffer, flowType, effectiveContext, operationToken)
                .ConfigureAwait(false),
            ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(protocolSystem,
                    logicalOperationId, serviceType, plainBuffer, flowType, effectiveContext, operationToken)
                .ConfigureAwait(false),
            _ => Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Unsupported flow type: {flowType}"))
        };
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

    private async Task RetryPendingRequestsAfterRecovery()
    {
        bool acquired = false;
        try
        {
            await _retryPendingRequestsGate.WaitAsync(_shutdownCancellationToken.Token).ConfigureAwait(false);
            acquired = true;
            await pendingRequestManager.RetryAllPendingRequestsAsync().ConfigureAwait(false);
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
        AutoRetry,
        ManualRetry,
        DirectNoRetry
    }

    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelAsync(
        EcliptixSessionState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings,
        RestoreRetryMode retryMode = RestoreRetryMode.AutoRetry,
        bool enablePendingRegistration = true,
        CancellationToken cancellationToken = default)
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? AppCultureSettingsConstants.DEFAULT_CULTURE_CODE
            : applicationInstanceSettings.Culture;

        rpcMetaDataProvider.SetAppInfo(
            Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId),
            culture);

        RestoreChannelRequest request = new();

        Result<RestoreChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse;

        switch (retryMode)
        {
            case RestoreRetryMode.AutoRetry:
                {
                    BeginSecrecyChannelEstablishRecovery();
                    CancellationToken recoveryToken = GetConnectionRecoveryToken();
                    using CancellationTokenSource combinedCancellationTokenSource =
                        cancellationToken.CanBeCanceled
                            ? CancellationTokenSource.CreateLinkedTokenSource(recoveryToken, cancellationToken)
                            : CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

                    restoreAppDeviceSecrecyChannelResponse = await retryStrategy.ExecuteRpcOperationAsync(
                        (_, ct) => rpcServiceManager.RestoreSecrecyChannelAsync(connectivityService,
                            request,
                            cancellationToken: ct),
                        "RestoreSecrecyChannel",
                        ecliptixSecrecyChannelState.ConnectId,
                        serviceType: RpcServiceType.RestoreSecrecyChannel,
                        cancellationToken: combinedCancellationTokenSource.Token).ConfigureAwait(false);
                    break;
                }
            case RestoreRetryMode.ManualRetry:
                {
                    BeginSecrecyChannelEstablishRecovery();
                    CancellationToken recoveryToken = GetConnectionRecoveryToken();
                    using CancellationTokenSource combinedCts =
                        cancellationToken.CanBeCanceled
                            ? CancellationTokenSource.CreateLinkedTokenSource(recoveryToken, cancellationToken)
                            : CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

                    restoreAppDeviceSecrecyChannelResponse = await retryStrategy.ExecuteManualRetryRpcOperationAsync(
                        (_, ct) => rpcServiceManager.RestoreSecrecyChannelAsync(connectivityService,
                            request,
                            cancellationToken: ct),
                        "RestoreSecrecyChannel",
                        ecliptixSecrecyChannelState.ConnectId,
                        cancellationToken: combinedCts.Token).ConfigureAwait(false);
                    break;
                }
            case RestoreRetryMode.DirectNoRetry:
                try
                {
                    restoreAppDeviceSecrecyChannelResponse =
                        await rpcServiceManager.RestoreSecrecyChannelAsync(connectivityService,
                            request,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Result<bool, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(ex.Message));
                }

                break;
            default:
                return Result<bool, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"Unknown retry mode: {retryMode}"));
        }

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
        {
            NetworkFailure failure = restoreAppDeviceSecrecyChannelResponse.UnwrapErr();

            if (enablePendingRegistration && ShouldQueueSecrecyChannelRetry(failure))
            {
                QueueSecrecyChannelRestoreRetry(ecliptixSecrecyChannelState, applicationInstanceSettings, retryMode);
            }

            return Result<bool, NetworkFailure>.Err(failure);
        }

        RestoreChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        switch (response.Status)
        {
            case RestoreChannelResponse.Types.Status.SessionRestored:
                {
                    Result<Unit, EcliptixProtocolFailure>
                        syncResult = SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

                    if (syncResult.IsErr)
                    {
                        EcliptixProtocolFailure error = syncResult.UnwrapErr();
                        return error.Message.Contains("Session validation failed")
                            ? Result<bool, NetworkFailure>.Ok(false)
                            : Result<bool, NetworkFailure>.Err(error.ToNetworkFailure());
                    }

                    if (enablePendingRegistration)
                    {
                        pendingRequestManager.RemovePendingRequest(
                            BuildSecrecyChannelRestoreKey(ecliptixSecrecyChannelState.ConnectId));
                    }

                    ExitOutage();

                    return Result<bool, NetworkFailure>.Ok(true);
                }
            case RestoreChannelResponse.Types.Status.SessionNotFound:
                {
                    Result<EcliptixSessionState, NetworkFailure> establishResult =
                        await EstablishSecrecyChannelAsync(ecliptixSecrecyChannelState.ConnectId);
                    if (establishResult.IsErr)
                    {
                        Log.Warning("[NETWORK-PROVIDER] Failed to establish secrecy channel after SESSION_NOT_FOUND: {ERROR}",
                            establishResult.UnwrapErr().Message);
                    }
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException();
        }

        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSessionState, NetworkFailure>> EstablishSecrecyChannelAsync(
        uint connectId)
    {
        Result<Option<EcliptixSessionState>, NetworkFailure> result =
            await EstablishSecrecyChannelInternalAsync(
                connectId,
                PubKeyExchangeType.DataCenterEphemeralConnect,
                saveState: true).ConfigureAwait(false);

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
        PubKeyExchangeType EXCHANGE_TYPE)
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application not initialized"));
        }

        ApplicationInstanceSettings appSettings = _applicationInstanceSettings.Value!;
        uint connectId = ComputeUniqueConnectId(appSettings, EXCHANGE_TYPE);

        if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? existingConnection))
        {
            existingConnection.Dispose();
        }

        InitiateEcliptixProtocolSystemForType(connectId);

        Result<Option<EcliptixSessionState>, NetworkFailure> establishOptionResult =
            await EstablishSecrecyChannelForTypeAsync(connectId, EXCHANGE_TYPE).ConfigureAwait(false);

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

    private static RatchetConfig GetRatchetConfigForExchangeType(PubKeyExchangeType EXCHANGE_TYPE)
    {
        RatchetConfig config = EXCHANGE_TYPE switch
        {
            PubKeyExchangeType.ServerStreaming => new RatchetConfig
            {
                DhRatchetEveryNMessages = 20,
                MaxChainAge = TimeSpan.FromMinutes(5),
                MaxMessagesWithoutRatchet = 100
            },
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

        foreach (PubKeyExchangeType EXCHANGE_TYPE in knownTypes)
        {
            uint computedConnectId = ComputeUniqueConnectId(applicationInstanceSettings, EXCHANGE_TYPE);
            if (computedConnectId != connectId)
            {
                continue;
            }

            return EXCHANGE_TYPE;
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
        PubKeyExchangeType EXCHANGE_TYPE)
    {
        return await EstablishSecrecyChannelInternalAsync(
            connectId,
            EXCHANGE_TYPE,
            maxRetries: 15,
            saveState: EXCHANGE_TYPE == PubKeyExchangeType.DataCenterEphemeralConnect).ConfigureAwait(false);
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

        PubKeyExchangeType EXCHANGE_TYPE = _applicationInstanceSettings.IsSome
            ? DetermineExchangeTypeFromConnectId(_applicationInstanceSettings.Value!, state.ConnectId)
            : PubKeyExchangeType.DataCenterEphemeralConnect;

        RatchetConfig config = GetRatchetConfigForExchangeType(EXCHANGE_TYPE);

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> connResult =
            EcliptixProtocolConnection.FromProtoState(state.ConnectId, state.RatchetState, config, EXCHANGE_TYPE);

        if (connResult.IsErr)
        {
            idKeysResult.Unwrap().Dispose();
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
        }

        return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap());
    }

    private static uint GenerateLogicalOperationId(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
    {
        Span<byte> hashBuffer = stackalloc byte[NetworkConstants.Cryptography.SHA_256_HASH_SIZE];

        switch (serviceType.ToString())
        {
            case "OpaqueSignInInitRequest" or "OpaqueSignInFinalizeRequest":
                {
                    Span<byte> semanticBuffer = stackalloc byte[256];
                    int written = System.Text.Encoding.UTF8.GetBytes($"auth:signin:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
            case "OpaqueSignUpInitRequest" or "OpaqueSignUpFinalizeRequest":
                {
                    Span<byte> semanticBuffer = stackalloc byte[256];
                    int written = System.Text.Encoding.UTF8.GetBytes($"auth:signup:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
            case "InitiateVerification":
                {
                    Span<byte> payloadHash = stackalloc byte[NetworkConstants.Cryptography.SHA_256_HASH_SIZE];
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
                    Span<byte> payloadHash = stackalloc byte[NetworkConstants.Cryptography.SHA_256_HASH_SIZE];
                    SHA256.HashData(plainBuffer, payloadHash);

                    string semantic = $"data:{serviceType}:{connectId}:{Convert.ToHexString(payloadHash)}";
                    Span<byte> semanticBuffer = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(semantic)];
                    int written = System.Text.Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
        }

        const int HASH_LENGTH = NetworkConstants.Cryptography.SHA_256_HASH_SIZE;

        uint rawId = BitConverter.ToUInt32(hashBuffer[..HASH_LENGTH]);
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

            invokeResult = await retryStrategy.ExecuteRpcOperationAsync(
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

                    return rpcServiceManager.InvokeServiceRequestAsync(request, ct);
                },
                $"UnaryRequest_{serviceType}",
                connectId,
                serviceType: serviceType,
                maxRetries: Math.Max(0, retryBehavior.MAX_ATTEMPTS - 1),
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
            invokeResult = await rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);
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
            Log.Error("[CLIENT-DECRYPT-ERROR] Decryption failed. ERROR: {ERROR}", decryptedData.UnwrapErr().Message);
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

            return await retryStrategy.ExecuteRpcOperationAsync(
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
                maxRetries: Math.Max(0, retryBehavior.MAX_ATTEMPTS - 1),
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
            await rpcServiceManager.InvokeServiceRequestAsync(request, linkedTokenSource.Token).ConfigureAwait(false);

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
            _ = connectivityService.PublishAsync(
                ConnectivityIntent.Disconnected(failure, connectId)).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing disconnected event");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private static async Task<Result<byte[], NetworkFailure>> ProcessStreamItemAsync(
        SecureEnvelope envelope,
        EcliptixProtocolSystem protocolSystem,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem)
    {
        Result<byte[], EcliptixProtocolFailure> decryptResult =
            protocolSystem.ProcessInboundEnvelope(envelope);

        if (decryptResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Failed to decrypt stream item"));
        }

        byte[] decryptedData = decryptResult.Unwrap();
        Result<Unit, NetworkFailure> itemResult = await onStreamItem(decryptedData).ConfigureAwait(false);

        if (itemResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(itemResult.UnwrapErr());
        }

        return Result<byte[], NetworkFailure>.Ok(decryptedData);
    }

    private void CleanupActiveStream(uint connectId)
    {
        _activeStreams.TryRemove(connectId, out _);
    }

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
            _ = connectivityService.PublishAsync(
                ConnectivityIntent.Connected(connectId)).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
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
            await rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
            await rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

        if (invokeResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.OutboundSink)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected OutboundSink flow but received {flow.GetType().Name}"));
        }

        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Client streaming is not yet implemented"));
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
            await rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
            connectivityService.PublishAsync(
                ConnectivityIntent.Connected()).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
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

            connectivityService.PublishAsync(
                ConnectivityIntent.Recovering(
                    NetworkFailure.DataCenterNotResponding("Recovering secrecy channel"))).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
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
            try
            {
                await TryPersistProtocolStateAsync(connectId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    Log.Error(ex, "[CLIENT-PROTOCOL-PERSIST] Failed to persist session state in background");
                }
            }
        }, _shutdownCancellationToken.Token).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception in protocol state persistence");
                }
            },
            TaskScheduler.Default);
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

    private static Option<EcliptixSessionState> BuildSessionState(uint connectId, EcliptixProtocolSystem protocolSystem, EcliptixProtocolConnection connection)
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

                foreach (KeyValuePair<string, CancellationTokenSource> kv in _pendingRequests.ToArray())
                {
                    if (!_pendingRequests.TryRemove(kv.Key, out CancellationTokenSource? cts))
                    {
                        continue;
                    }

                    try
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Intentionally suppressed: Operation cancellation token source already disposed
                    }
                }

                foreach (KeyValuePair<uint, CancellationTokenSource> kv in _activeStreams.ToArray())
                {
                    if (!_activeStreams.TryRemove(kv.Key, out CancellationTokenSource? streamCts))
                    {
                        continue;
                    }

                    try
                    {
                        streamCts.Cancel();
                        streamCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Intentionally suppressed: Stream cancellation token source already disposed
                    }
                }

                lock (_outageLock)
                {
                    _outageCompletionSource.TrySetException(new OperationCanceledException("Provider shutting down"));
                }

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
                        // Continue disposing other connections even if one fails
                    }
                }

                lock (_cancellationLock)
                {
                    _connectionRecoveryCts?.Dispose();
                    _connectionRecoveryCts = null;
                }

                foreach (SemaphoreSlim gate in _channelGates.Values)
                {
                    gate.Dispose();
                }

                _channelGates.Clear();

                _retryPendingRequestsGate.Dispose();

                _shutdownCancellationToken.Dispose();
            }
            catch (Exception ex)
            {
                // Final safety catch for any disposal errors
                Log.Warning(ex, "[NETWORK-PROVIDER] ERROR during final disposal cleanup");
            }
        }
    }

    public async Task<Result<Unit, NetworkFailure>> ForceFreshConnectionAsync()
    {
        retryStrategy.ClearExhaustedOperations();

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
            RestoreRetryMode.ManualRetry,
            "Manual retry restoration failed").ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformImmediateRecoveryLogic()
    {
        return await PerformRecoveryWithStateRestorationAsync(
            RestoreRetryMode.DirectNoRetry,
            NetworkConstants.ErrorMessages.SESSION_NOT_FOUND_ON_SERVER,
            failOnMissingState: true).ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformRecoveryWithStateRestorationAsync(
        RestoreRetryMode retryMode,
        string failureMessage,
        bool failOnMissingState = false)
    {
        Result<(uint connectId, byte[] membershipId), NetworkFailure> prerequisitesResult = ValidateRecoveryPrerequisites();
        if (prerequisitesResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(prerequisitesResult.UnwrapErr());
        }

        (uint connectId, byte[] membershipId) = prerequisitesResult.Unwrap();
        _connections.TryRemove(connectId, out _);

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            await LoadAndParseStoredState(connectId, membershipId, failOnMissingState, failureMessage).ConfigureAwait(false);

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
            if (retryMode == RestoreRetryMode.DirectNoRetry)
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
            Log.Warning("[CLIENT-RECOVERY-STATE] Cannot load state: membershipId not available. ConnectId: {ConnectId}",
                connectId);
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
            await secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);

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
            _ = connectivityService.PublishAsync(
                ConnectivityIntent.Connected(connectId)).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Log.Error(task.Exception, "[NETWORK-PROVIDER] Unhandled exception publishing connected event after restoration");
                    }
                },
                TaskScheduler.Default);
        });
    }

    private void ResetRetryStrategyAfterOutage()
    {
        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in _connections)
        {
            retryStrategy.MarkConnectionHealthy(connection.Key);
        }
    }

    private static string BuildSecrecyChannelPendingKey(uint connectId, PubKeyExchangeType EXCHANGE_TYPE) =>
        $"secrecy-channel:{connectId}:{EXCHANGE_TYPE}";

    private static string BuildSecrecyChannelRestoreKey(uint connectId) =>
        $"secrecy-channel-restore:{connectId}";

    private static bool ShouldQueueSecrecyChannelRetry(NetworkFailure failure)
    {
        return failure.FailureType is NetworkFailureType.DATA_CENTER_NOT_RESPONDING
            or NetworkFailureType.DATA_CENTER_SHUTDOWN
            or NetworkFailureType.PROTOCOL_STATE_MISMATCH
            or NetworkFailureType.RSA_ENCRYPTION_FAILURE;
    }

    private void QueueSecrecyChannelEstablishRetry(uint connectId, PubKeyExchangeType EXCHANGE_TYPE, int? maxRetries,
        bool saveState)
    {
        if (_disposed)
        {
            return;
        }

        BeginSecrecyChannelEstablishRecovery();

        string pendingKey = BuildSecrecyChannelPendingKey(connectId, EXCHANGE_TYPE);

        pendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
        {
            CancellationToken recoveryToken = GetConnectionRecoveryToken();
            using CancellationTokenSource linkedCts =
                CancellationTokenSource.CreateLinkedTokenSource(ct, recoveryToken);

            await EstablishSecrecyChannelInternalAsync(
                    connectId,
                    EXCHANGE_TYPE,
                    maxRetries,
                    saveState,
                    cancellationToken: linkedCts.Token,
                    enablePendingRegistration: false)
                .ConfigureAwait(false);
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

        pendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
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
            Log.Warning("[CLIENT-STATE-PERSIST] Cannot persist: membershipId not available. ConnectId: {ConnectId}",
                connectId);
            return;
        }

        Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                state.ToByteString(),
                span => secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(), membershipId))
            .ConfigureAwait(false);

        if (saveResult.IsErr)
        {
            Log.Warning("[CLIENT-STATE-PERSIST] Failed to save session state. ConnectId: {ConnectId}, ERROR: {ERROR}",
                connectId, saveResult.UnwrapErr().Message);
        }

        string timestampKey = $"{connectId}_timestamp";
        await applicationSecureStorageProvider.StoreAsync(timestampKey,
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

                    Result<Option<EcliptixSessionState>, NetworkFailure> reEstablishResult =
                        await EstablishSecrecyChannelInternalAsync(
                            connectId,
                            PubKeyExchangeType.DataCenterEphemeralConnect,
                            saveState: false,
                            enablePendingRegistration: false).ConfigureAwait(false);

                    return reEstablishResult.IsOk
                        ? Result<bool, NetworkFailure>.Ok(true)
                        : Result<bool, NetworkFailure>.Ok(false);
                }

                Result<byte[], SecureStorageFailure> stateResult =
                    await secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
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
            retryPolicyProvider.GetRetryBehavior(RpcServiceType.EstablishAuthenticatedSecureChannel);
        Result<Unit, NetworkFailure> networkResult = await retryStrategy.ExecuteRpcOperationAsync(
            async (_, _) => await RecreateProtocolWithMasterKeyAsyncInternal(
                masterKeyHandle,
                membershipIdentifier,
                connectId).ConfigureAwait(false),
            "RecreateProtocolWithMasterKey",
            connectId,
            serviceType: RpcServiceType.EstablishAuthenticatedSecureChannel,
            maxRetries: retryBehavior.MAX_ATTEMPTS - 1,
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

            if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? oldProtocol))
            {
                CancelOperationsForConnection(connectId);
                oldProtocol.Dispose();
            }

            const PubKeyExchangeType EXCHANGE_TYPE = PubKeyExchangeType.DataCenterEphemeralConnect;
            EcliptixProtocolSystem? newProtocol = new(identityKeys);

            try
            {
                newProtocol.SetEventHandler(this);

                Result<PubKeyExchange, EcliptixProtocolFailure> clientExchangeResult =
                    newProtocol.BeginDataCenterPubKeyExchange(connectId, EXCHANGE_TYPE);

                if (clientExchangeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        clientExchangeResult.UnwrapErr().ToNetworkFailure());
                }

                PubKeyExchange clientExchange = clientExchangeResult.Unwrap();

                AuthenticatedEstablishRequest authenticatedRequest = new()
                {
                    MembershipUniqueId = membershipIdentifier,
                    ClientPubKeyExchange = clientExchange
                };

                Result<SecureEnvelope, NetworkFailure> serverResponseResult =
                    await rpcServiceManager.EstablishAuthenticatedSecureChannelAsync(
                        connectivityService, authenticatedRequest).ConfigureAwait(false);

                if (serverResponseResult.IsErr)
                {
                    NetworkFailure failure = serverResponseResult.UnwrapErr();
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(failure);
                }

                SecureEnvelope responseEnvelope = serverResponseResult.Unwrap();

                Option<CertificatePinningService> certificatePinningService =
                    await certificatePinningServiceFactory.GetOrInitializeServiceAsync();

                if (!certificatePinningService.IsSome)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
                }

                byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();
                Result<byte[], NetworkFailure> decryptResult =
                    rsaChunkEncryptor.DecryptInChunks(certificatePinningService.Value!, combinedEncryptedResponse);

                if (decryptResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(decryptResult.UnwrapErr());
                }

                PubKeyExchange serverExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

                Result<Unit, EcliptixProtocolFailure> completeResult =
                    newProtocol.CompleteAuthenticatedPubKeyExchange(serverExchange, rootKeyBytes);

                if (completeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        completeResult.UnwrapErr().ToNetworkFailure());
                }

                _connections.TryAdd(connectId, newProtocol);

                EcliptixProtocolConnection? connection = newProtocol.GetConnection();
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

                newProtocol = null;
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
            finally
            {
                newProtocol?.Dispose();
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
            await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning(
                "[CLIENT-AUTH-CLEANUP] Failed to delete protocol state during authentication cleanup. ConnectId: {ConnectId}, ERROR: {ERROR}",
                connectId, deleteResult.UnwrapErr().Message);
        }
    }
}
