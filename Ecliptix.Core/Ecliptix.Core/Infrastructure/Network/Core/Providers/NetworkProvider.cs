using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Core.Infrastructure.Security.Crypto;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Unit = Ecliptix.Utilities.Unit;
using System.Security.Cryptography;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed class NetworkProvider : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly IRpcServiceManager _rpcServiceManager;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly ISecureProtocolStateStorage _secureProtocolStateStorage;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private readonly INetworkEventService _networkEvents;
    private readonly ISystemEventService _systemEvents;
    private readonly IRetryStrategy _retryStrategy;
    private readonly IPendingRequestManager _pendingRequestManager;
    private readonly ICertificatePinningServiceFactory _certificatePinningServiceFactory;
    private readonly IRsaChunkEncryptor _rsaChunkEncryptor;

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeStreams = new();


    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlightRequests = new();

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelInternalAsync(
        uint connectId,
        PubKeyExchangeType exchangeType,
        int? maxRetries = null,
        bool saveState = true,
        CancellationToken cancellationToken = default)
    {
        if (exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnecting);
            });
        }

        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, exchangeType);

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

        CertificatePinningService? certificatePinningService =
            _certificatePinningServiceFactory.GetOrInitializeService();

        if (certificatePinningService == null)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        byte[] originalData = pubKeyExchangeRequest.Unwrap().ToByteArray();

        Result<byte[], NetworkFailure> encryptResult =
            _rsaChunkEncryptor.EncryptInChunks(certificatePinningService, originalData);
        if (encryptResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        byte[] combinedEncryptedPayload = encryptResult.Unwrap();

        SecureEnvelope envelope = EnvelopeBuilder.CreateSecureEnvelope(
            metadata,
            ByteString.CopyFrom(combinedEncryptedPayload)
        );

        CancellationToken finalToken = cancellationToken == default ? GetConnectionRecoveryToken() : cancellationToken;

        Result<SecureEnvelope, NetworkFailure> establishAppDeviceSecrecyChannelResult;
        if (maxRetries.HasValue)
        {
            establishAppDeviceSecrecyChannelResult = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.EstablishAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents, envelope,
                    exchangeType),
                "EstablishSecrecyChannel",
                connectId,
                maxRetries: maxRetries.Value,
                cancellationToken: finalToken).ConfigureAwait(false);
        }
        else
        {
            establishAppDeviceSecrecyChannelResult = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.EstablishAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents, envelope,
                    exchangeType),
                "EstablishSecrecyChannel",
                connectId,
                cancellationToken: finalToken).ConfigureAwait(false);
        }

        if (establishAppDeviceSecrecyChannelResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(establishAppDeviceSecrecyChannelResult
                .UnwrapErr());
        }

        SecureEnvelope responseEnvelope = establishAppDeviceSecrecyChannelResult.Unwrap();

        if (exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            CertificatePinningBoolResult certificatePinningBoolResult = certificatePinningService.VerifyServerSignature(
                responseEnvelope.EncryptedPayload.Memory,
                responseEnvelope.AuthenticationTag.Memory);

            if (!certificatePinningBoolResult.IsSuccess)
            {
                return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption(
                        $"Server signature verification failed: {certificatePinningBoolResult.Error?.Message}"));
            }
        }

        byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();

        Result<byte[], NetworkFailure> decryptResult =
            _rsaChunkEncryptor.DecryptInChunks(certificatePinningService, combinedEncryptedResponse);
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

        if (!saveState || exchangeType != PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None);
        }

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                new NetworkFailure(NetworkFailureType.DataCenterNotResponding,
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

        return ecliptixSecrecyChannelStateResult.ToNetworkFailure().Map(Option<EcliptixSessionState>.Some);
    }

    private readonly Lock _cancellationLock = new();
    private CancellationTokenSource? _connectionRecoveryCts;

    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _channelGates = new();

    private readonly SemaphoreSlim _retryPendingRequestsGate = new(1, 1);

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    private int _outageState;
    private readonly Lock _outageLock = new();
    private TaskCompletionSource<bool> _outageRecoveredTcs = CreateOutageTcs();

    private static TaskCompletionSource<bool> CreateOutageTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _disposed;
    private readonly Lock _disposeLock = new();

    public NetworkProvider(
        IRpcServiceManager rpcServiceManager,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        ISecureProtocolStateStorage secureProtocolStateStorage,
        IRpcMetaDataProvider rpcMetaDataProvider,
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        IRetryStrategy retryStrategy,
        IPendingRequestManager pendingRequestManager,
        ICertificatePinningServiceFactory certificatePinningServiceFactory,
        IRsaChunkEncryptor rsaChunkEncryptor)
    {
        _rpcServiceManager = rpcServiceManager;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _secureProtocolStateStorage = secureProtocolStateStorage;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _networkEvents = networkEvents;
        _systemEvents = systemEvents;
        _retryStrategy = retryStrategy;
        _pendingRequestManager = pendingRequestManager;
        _certificatePinningServiceFactory = certificatePinningServiceFactory;
        _rsaChunkEncryptor = rsaChunkEncryptor;
    }

    private readonly Lock _appInstanceSetterLock = new();

    public void SetCountry(string country)
    {
        lock (_appInstanceSetterLock)
        {
            if (_applicationInstanceSettings.Value != null)
                _applicationInstanceSettings.Value.Country = country;
        }
    }

    public ApplicationInstanceSettings ApplicationInstanceSettings =>
        _applicationInstanceSettings.Value!;

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType)
    {
        Guid appInstanceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);

        string appInstanceIdString = appInstanceGuid.ToString();
        string deviceIdString = deviceGuid.ToString();

        uint connectId = Helpers.ComputeUniqueConnectId(
            appInstanceIdString,
            deviceIdString, pubKeyExchangeType);

        return connectId;
    }

    public uint ComputeUniqueConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        return ComputeUniqueConnectId(_applicationInstanceSettings.Value!, pubKeyExchangeType);
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(NetworkConstants.Protocol.DefaultOneTimeKeyCount).Unwrap();

        PubKeyExchangeType exchangeType = DetermineExchangeTypeFromConnectId(applicationInstanceSettings, connectId);

        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        protocolSystem.SetEventHandler(this);

        _connections.TryAdd(connectId, protocolSystem);

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);
        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? AppCultureSettingsConstants.DefaultCultureCode
            : applicationInstanceSettings.Culture;

        _rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? system)) return;
        system.Dispose();
    }

    public bool HasConnection(uint connectId) => _connections.ContainsKey(connectId);

    public async Task<Result<Unit, NetworkFailure>> ExecuteUnaryRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false,
        CancellationToken token = default,
        bool waitForRecovery = true)
    {
        return await ExecuteServiceRequestInternalAsync(
            connectId, serviceType, plainBuffer, ServiceFlowType.Single,
            onCompleted, allowDuplicates, token, waitForRecovery).ConfigureAwait(false);
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
        bool allowDuplicates = false,
        CancellationToken token = default,
        bool waitForRecovery = true)
    {
        string requestKey;
        if (serviceType is RpcServiceType.OpaqueSignInInitRequest or RpcServiceType.OpaqueSignInCompleteRequest)
        {
            requestKey = $"{connectId}_{serviceType}_auth_operation";
        }
        else
        {
            int bytesToHash = Math.Min(plainBuffer.Length, NetworkConstants.Protocol.RequestKeyHexPrefixLength / 2);
            Span<char> hexBuffer = stackalloc char[NetworkConstants.Protocol.RequestKeyHexPrefixLength];
            bool success = Convert.TryToHexString(plainBuffer.AsSpan(0, bytesToHash), hexBuffer, out int charsWritten);
            requestKey = success
                ? $"{connectId}_{serviceType}_{hexBuffer[..charsWritten].ToString()}"
                : $"{connectId}_{serviceType}_fallback";
        }

        bool shouldAllowDuplicates = allowDuplicates || ShouldAllowDuplicateRequests(serviceType);
        CancellationTokenSource perRequestCts = new();
        if (!shouldAllowDuplicates && !_inFlightRequests.TryAdd(requestKey, perRequestCts))
        {
            perRequestCts.Dispose();
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Duplicate request rejected"));
        }

        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(token, perRequestCts.Token);
        CancellationToken operationToken = linkedCts.Token;

        try
        {
            await WaitForOutageRecoveryAsync(operationToken, waitForRecovery).ConfigureAwait(false);

            operationToken.ThrowIfCancellationRequested();

            if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
            {
                NetworkFailure noConnectionFailure = NetworkFailure.DataCenterNotResponding(
                    "Connection unavailable - server may be recovering");
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ServerShutdown);
                return Result<Unit, NetworkFailure>.Err(noConnectionFailure);
            }

            uint logicalOperationId = GenerateLogicalOperationId(connectId, serviceType, plainBuffer);
            Result<ServiceRequest, NetworkFailure> requestResult =
                BuildRequestWithId(protocolSystem, logicalOperationId, serviceType, plainBuffer, flowType);

            if (requestResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(requestResult.UnwrapErr());
            }

            ServiceRequest request = requestResult.Unwrap();

            try
            {
                Result<Unit, NetworkFailure> networkResult = flowType switch
                {
                    ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request, onCompleted,
                        operationToken).ConfigureAwait(false),
                    ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem, request,
                        onCompleted, operationToken).ConfigureAwait(false),
                    ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, request, onCompleted,
                        operationToken).ConfigureAwait(false),
                    ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(protocolSystem,
                        request, onCompleted, operationToken).ConfigureAwait(false),
                    _ => Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType($"Unsupported flow type: {flowType}"))
                };

                if (!networkResult.IsOk || Volatile.Read(ref _outageState) == 0) return networkResult;
                ExitOutage();

                return networkResult;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
            catch (OperationCanceledException) when (flowType == ServiceFlowType.ReceiveStream)
            {
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(ex.Message));
            }
        }
        finally
        {
            if (!shouldAllowDuplicates && _inFlightRequests.TryRemove(requestKey, out CancellationTokenSource? cts))
            {
                cts.Dispose();
            }
        }
    }

    private async Task PerformReconnectionLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
            return;
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Unit, NetworkFailure> establishResult = await EstablishConnectionOnly(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
        });
        Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task RetryPendingRequestsAfterRecovery()
    {
        await _retryPendingRequestsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _pendingRequestManager.RetryAllPendingRequestsAsync().ConfigureAwait(false);
        }
        finally
        {
            _retryPendingRequestsGate.Release();
        }
    }

    private async Task<Result<Unit, NetworkFailure>> EstablishConnectionOnly(uint connectId)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        _connections.TryRemove(connectId, out _);

        InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        await PersistSessionStateAsync(secrecyChannelState, connectId).ConfigureAwait(false);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
        });
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
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
        RestoreRetryMode retryMode = RestoreRetryMode.AutoRetry)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? AppCultureSettingsConstants.DefaultCultureCode
            : applicationInstanceSettings.Culture;

        _rpcMetaDataProvider.SetAppInfo(
            Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId),
            culture);

        RestoreChannelRequest request = new();

        Result<RestoreChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse;

        switch (retryMode)
        {
            case RestoreRetryMode.AutoRetry:
                {
                    CancellationToken recoveryToken = GetConnectionRecoveryToken();
                    using CancellationTokenSource combinedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

                    restoreAppDeviceSecrecyChannelResponse = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                        () => _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                            request),
                        "RestoreSecrecyChannel",
                        ecliptixSecrecyChannelState.ConnectId,
                        cancellationToken: combinedCts.Token).ConfigureAwait(false);
                    break;
                }
            case RestoreRetryMode.ManualRetry:
                {
                    CancellationToken recoveryToken = GetConnectionRecoveryToken();
                    using CancellationTokenSource combinedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

                    restoreAppDeviceSecrecyChannelResponse = await _retryStrategy.ExecuteManualRetryOperationAsync(
                        () => _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                            request),
                        "RestoreSecrecyChannel",
                        ecliptixSecrecyChannelState.ConnectId,
                        cancellationToken: combinedCts.Token).ConfigureAwait(false);
                    break;
                }
            case RestoreRetryMode.DirectNoRetry:
                try
                {
                    restoreAppDeviceSecrecyChannelResponse =
                        await _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                            request).ConfigureAwait(false);
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
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());
        }

        RestoreChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreChannelResponse.Types.Status.SessionRestored)
        {
            Result<Unit, EcliptixProtocolFailure>
                syncResult = SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

            if (syncResult.IsErr)
            {
                EcliptixProtocolFailure error = syncResult.UnwrapErr();
                if (error.Message.Contains("Session validation failed"))
                {
                    Log.Information("[CLIENT-RESTORE] Session validation failed, will attempt fresh handshake. ConnectId: {ConnectId}",
                        ecliptixSecrecyChannelState.ConnectId);
                    return Result<bool, NetworkFailure>.Ok(false);
                }

                return Result<bool, NetworkFailure>.Err(error.ToNetworkFailure());
            }

            return Result<bool, NetworkFailure>.Ok(true);
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
        if (!stateOption.HasValue)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Failed to create session state"));
        }

        return Result<EcliptixSessionState, NetworkFailure>.Ok(stateOption.Value!);
    }

    public async Task<Result<uint, NetworkFailure>> EnsureProtocolForTypeAsync(
        PubKeyExchangeType exchangeType)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application not initialized"));
        }

        ApplicationInstanceSettings appSettings = _applicationInstanceSettings.Value!;
        uint connectId = ComputeUniqueConnectId(appSettings, exchangeType);

        if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? existingConnection))
        {
            _connections.TryRemove(connectId, out _);
            existingConnection.Dispose();
        }

        InitiateEcliptixProtocolSystemForType(connectId, exchangeType);

        Result<Option<EcliptixSessionState>, NetworkFailure> establishOptionResult =
            await EstablishSecrecyChannelForTypeAsync(connectId, exchangeType).ConfigureAwait(false);

        if (establishOptionResult.IsErr)
        {
            _connections.TryRemove(connectId, out _);
            return Result<uint, NetworkFailure>.Err(establishOptionResult.UnwrapErr());
        }

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    private void CancelOperationsForConnection(uint connectId)
    {
        string connectIdPrefix = $"{connectId}_";

        foreach (KeyValuePair<string, CancellationTokenSource> kvp in _inFlightRequests)
        {
            if (!kvp.Key.StartsWith(connectIdPrefix)) continue;
            if (!_inFlightRequests.TryRemove(kvp.Key, out CancellationTokenSource? operationCts)) continue;
            try
            {
                operationCts.Cancel();
                operationCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task<Result<Unit, NetworkFailure>> CleanupStreamProtocolAsync(uint connectId)
    {
        CancelOperationsForConnection(connectId);

        if (_activeStreams.TryRemove(connectId, out CancellationTokenSource? streamCts))
        {
            await streamCts.CancelAsync().ConfigureAwait(false);
            streamCts.Dispose();
        }

        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? protocolSystem))
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);

        protocolSystem.Dispose();

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private static RatchetConfig GetRatchetConfigForExchangeType(PubKeyExchangeType exchangeType)
    {
        RatchetConfig config = exchangeType switch
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

        foreach (PubKeyExchangeType exchangeType in knownTypes)
        {
            uint computedConnectId = ComputeUniqueConnectId(applicationInstanceSettings, exchangeType);
            if (computedConnectId != connectId) continue;

            return exchangeType;
        }

        return PubKeyExchangeType.DataCenterEphemeralConnect;
    }

    private void InitiateEcliptixProtocolSystemForType(uint connectId,
        PubKeyExchangeType exchangeType)
    {
        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(NetworkConstants.Protocol.DefaultOneTimeKeyCount).Unwrap();

        EcliptixProtocolSystem protocolSystem = new(identityKeys);
        protocolSystem.SetEventHandler(this);

        _connections.TryAdd(connectId, protocolSystem);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelForTypeAsync(
        uint connectId,
        PubKeyExchangeType exchangeType)
    {
        return await EstablishSecrecyChannelInternalAsync(
            connectId,
            exchangeType,
            maxRetries: 15,
            saveState: exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect).ConfigureAwait(false);
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
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not established"));

        Log.Information("[CLIENT-SYNC] Syncing with server. ConnectId: {ConnectId}, ServerSending: {ServerSending}, ServerReceiving: {ServerReceiving}",
            currentState.ConnectId, peerSecrecyChannelState.SendingChainLength, peerSecrecyChannelState.ReceivingChainLength);

        Result<Unit, EcliptixProtocolFailure> syncResult = connection.SyncWithRemoteState(
            peerSecrecyChannelState.SendingChainLength,
            peerSecrecyChannelState.ReceivingChainLength
        );

        if (syncResult.IsErr)
        {
            system.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(syncResult.UnwrapErr());
        }

        Result<RatchetState, EcliptixProtocolFailure> ratchetState = connection.ToProtoState();
        if (ratchetState.IsOk)
        {
            RatchetState state = ratchetState.Unwrap();
            Log.Information("[CLIENT-SYNC] Sync complete. ConnectId: {ConnectId}, ClientSending: {Sending}, ClientReceiving: {Receiving}",
                currentState.ConnectId, state.SendingStep.CurrentIndex, state.ReceivingStep.CurrentIndex);
        }

        _connections.TryAdd(currentState.ConnectId, system);
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<EcliptixProtocolSystem, EcliptixProtocolFailure> RecreateSystemFromState(
        EcliptixSessionState state)
    {
        Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> idKeysResult =
            EcliptixSystemIdentityKeys.FromProtoState(state.IdentityKeys);
        if (idKeysResult.IsErr)
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());

        PubKeyExchangeType exchangeType = _applicationInstanceSettings.HasValue
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

    private uint GenerateLogicalOperationId(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
    {
        Span<byte> hashBuffer = stackalloc byte[NetworkConstants.Cryptography.Sha256HashSize];
        int hashLength = 0;

        switch (serviceType.ToString())
        {
            case "OpaqueSignInInitRequest" or "OpaqueSignInFinalizeRequest":
                {
                    Span<byte> semanticBuffer = stackalloc byte[256];
                    int written = System.Text.Encoding.UTF8.GetBytes($"auth:signin:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    hashLength = NetworkConstants.Cryptography.Sha256HashSize;
                    break;
                }
            case "OpaqueSignUpInitRequest" or "OpaqueSignUpFinalizeRequest":
                {
                    Span<byte> semanticBuffer = stackalloc byte[256];
                    int written = System.Text.Encoding.UTF8.GetBytes($"auth:signup:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    hashLength = NetworkConstants.Cryptography.Sha256HashSize;
                    break;
                }
            case "InitiateVerification":
                {
                    Span<byte> payloadHash = stackalloc byte[NetworkConstants.Cryptography.Sha256HashSize];
                    SHA256.HashData(plainBuffer, payloadHash);

                    string semantic =
                        $"stream:{serviceType}:{connectId}:{DateTime.UtcNow.Ticks}:{Convert.ToHexString(payloadHash)}";
                    Span<byte> semanticBuffer = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(semantic)];
                    int written = System.Text.Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    hashLength = NetworkConstants.Cryptography.Sha256HashSize;
                    break;
                }
            default:
                {
                    Span<byte> payloadHash = stackalloc byte[NetworkConstants.Cryptography.Sha256HashSize];
                    SHA256.HashData(plainBuffer, payloadHash);

                    string semantic = $"data:{serviceType}:{connectId}:{Convert.ToHexString(payloadHash)}";
                    Span<byte> semanticBuffer = stackalloc byte[System.Text.Encoding.UTF8.GetByteCount(semantic)];
                    int written = System.Text.Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    hashLength = NetworkConstants.Cryptography.Sha256HashSize;
                    break;
                }
        }

        uint rawId = BitConverter.ToUInt32(hashBuffer[..hashLength]);
        uint finalId = Math.Max(rawId % (uint.MaxValue - NetworkConstants.Protocol.OperationIdReservedRange), NetworkConstants.Protocol.OperationIdMinValue);

        return finalId;
    }

    private static Result<ServiceRequest, NetworkFailure> BuildRequestWithId(
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType)
    {
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection != null)
        {
            Result<RatchetState, EcliptixProtocolFailure> stateResult = connection.ToProtoState();
            if (stateResult.IsOk)
            {
                RatchetState state = stateResult.Unwrap();
                Log.Information("[CLIENT-ENCRYPT-BEFORE] About to encrypt {ServiceType}. Sending: {Sending}, Receiving: {Receiving}",
                    serviceType, state.SendingStep.CurrentIndex, state.ReceivingStep.CurrentIndex);
            }
        }

        Result<SecureEnvelope, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundEnvelope(plainBuffer);

        if (outboundPayload.IsErr)
        {
            return Result<ServiceRequest, NetworkFailure>.Err(
                outboundPayload.UnwrapErr().ToNetworkFailure());
        }

        SecureEnvelope cipherPayload = outboundPayload.Unwrap();

        Log.Information("[CLIENT-ENCRYPT-ENVELOPE] Outgoing envelope for {ServiceType}. HeaderNonce: {HeaderNonce}, AuthTag: {AuthTag}",
            serviceType,
            Convert.ToHexString(cipherPayload.HeaderNonce.ToByteArray())[..Math.Min(16, cipherPayload.HeaderNonce.Length * 2)],
            cipherPayload.AuthenticationTag != null ? Convert.ToHexString(cipherPayload.AuthenticationTag.ToByteArray())[..Math.Min(16, cipherPayload.AuthenticationTag.Length * 2)] : "NULL");

        return Result<ServiceRequest, NetworkFailure>.Ok(
            ServiceRequest.New(logicalOperationId, flowType, serviceType, cipherPayload, []));
    }

    private async Task<Result<Unit, NetworkFailure>> SendUnaryRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

        if (invokeResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
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
            return Result<Unit, NetworkFailure>.Err(callResult.UnwrapErr());
        }

        SecureEnvelope inboundPayload = callResult.Unwrap();

        Log.Information("[CLIENT-DECRYPT-ENVELOPE] Incoming response envelope. HeaderNonce: {HeaderNonce}, AuthTag: {AuthTag}",
            Convert.ToHexString(inboundPayload.HeaderNonce.ToByteArray())[..Math.Min(16, inboundPayload.HeaderNonce.Length * 2)],
            inboundPayload.AuthenticationTag != null ? Convert.ToHexString(inboundPayload.AuthenticationTag.ToByteArray())[..Math.Min(16, inboundPayload.AuthenticationTag.Length * 2)] : "NULL");

        EcliptixProtocolConnection? conn = protocolSystem.GetConnection();
        if (conn != null)
        {
            Result<RatchetState, EcliptixProtocolFailure> beforeState = conn.ToProtoState();
            if (beforeState.IsOk)
            {
                RatchetState state = beforeState.Unwrap();
                Log.Information("[CLIENT-DECRYPT-BEFORE] Before decryption. Sending: {Sending}, Receiving: {Receiving}",
                    state.SendingStep.CurrentIndex, state.ReceivingStep.CurrentIndex);
            }
        }

        Result<byte[], EcliptixProtocolFailure> decryptedData =
            protocolSystem.ProcessInboundEnvelope(inboundPayload);
        if (decryptedData.IsErr)
        {
            Log.Error("[CLIENT-DECRYPT-ERROR] Decryption failed. Error: {Error}", decryptedData.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(decryptedData.UnwrapErr().ToNetworkFailure());
        }

        Log.Information("[CLIENT-DECRYPT-SUCCESS] Decryption succeeded.");

        if (conn != null)
        {
            Result<RatchetState, EcliptixProtocolFailure> afterState = conn.ToProtoState();
            if (afterState.IsOk)
            {
                RatchetState state = afterState.Unwrap();
                Log.Information("[CLIENT-DECRYPT-AFTER] After decryption. Sending: {Sending}, Receiving: {Receiving}",
                    state.SendingStep.CurrentIndex, state.ReceivingStep.CurrentIndex);
            }
        }

        await onCompleted(decryptedData.Unwrap()).ConfigureAwait(false);
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> SendReceiveStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        uint connectId = _connections.FirstOrDefault(kvp => kvp.Value == protocolSystem).Key;
        if (connectId == 0)
        {
            return await ProcessStreamDirectly(protocolSystem, request, onStreamItem, token).ConfigureAwait(false);
        }

        using CancellationTokenSource streamCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _activeStreams.TryAdd(connectId, streamCts);

        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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

        try
        {
            await foreach (Result<SecureEnvelope, NetworkFailure> streamItem in
                           inboundStream.Stream.WithCancellation(streamCts.Token))
            {
                if (streamItem.IsErr)
                {
                    NetworkFailure failure = streamItem.UnwrapErr();

                    if (failure.FailureType is NetworkFailureType.DataCenterNotResponding
                        or NetworkFailureType.DataCenterShutdown)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterDisconnected);
                        });
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(""));
                    }

                    return Result<Unit, NetworkFailure>.Err(failure);
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
        }
        catch (OperationCanceledException) when (streamCts.Token.IsCancellationRequested)
        {
        }
        finally
        {
            if (_activeStreams.TryRemove(connectId, out _))
            {
            }
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> ProcessStreamDirectly(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token).ConfigureAwait(false);

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
        if (Volatile.Read(ref _outageState) == 0) return;
        if (!waitForRecovery) return;

        Task waitTask;
        lock (_outageLock)
        {
            waitTask = _outageRecoveredTcs.Task;
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.Token.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(NetworkProvider), "Provider is shutting down");
        }
    }

    private void EnterOutage()
    {
        if (Interlocked.Exchange(ref _outageState, 1) == 1) return;

        lock (_outageLock)
        {
            _outageRecoveredTcs = CreateOutageTcs();
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterDisconnected);
            _ = _systemEvents.NotifySystemStateAsync(SystemState.Recovering);
        });
    }

    private void ExitOutage()
    {
        if (Interlocked.Exchange(ref _outageState, 0) == 0) return;

        lock (_outageLock)
        {
            if (!_outageRecoveredTcs.Task.IsCompleted)
                _outageRecoveredTcs.TrySetResult(true);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ConnectionRestored);
            _ = _systemEvents.NotifySystemStateAsync(SystemState.Running);
        });
    }

    private CancellationToken GetConnectionRecoveryToken()
    {
        lock (_cancellationLock)
        {
            return _connectionRecoveryCts?.Token ?? CancellationToken.None;
        }
    }

    private void CancelActiveRecoveryOperations()
    {
        lock (_cancellationLock)
        {
            if (_connectionRecoveryCts != null)
            {
                _connectionRecoveryCts.Cancel();
                _connectionRecoveryCts.Dispose();
            }

            _connectionRecoveryCts = new CancellationTokenSource();
        }
    }

    private void CancelActiveRequestsDuringRecovery()
    {
        if (_inFlightRequests.IsEmpty) return;

        foreach (KeyValuePair<string, CancellationTokenSource> kv in _inFlightRequests.ToArray())
        {
            if (!_inFlightRequests.TryRemove(kv.Key, out CancellationTokenSource? cts)) continue;
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task<T> WithChannelGate<T>(uint connectId, Func<Task<T>> action)
    {
        SemaphoreSlim gate = _channelGates.GetOrAdd(connectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task InitiateConnectionRecoveryWithCancellation(uint connectId)
    {
        try
        {
            CancelActiveRecoveryOperations();
            CancelActiveRequestsDuringRecovery();

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ConnectionRecovering);
            });

            if (!_applicationInstanceSettings.HasValue)
            {
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.RestoreSecrecyChannel);
            });

            byte[]? membershipId = GetMembershipIdBytes();
            if (membershipId == null)
            {
                Log.Warning("[CLIENT-RECOVERY] Cannot load state: membershipId not available. ConnectId: {ConnectId}",
                    connectId);
                return;
            }

            Result<byte[], SecureStorageFailure> stateResult =
                await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);

            bool restorationSuccessful;
            if (stateResult.IsOk)
            {
                try
                {
                    EcliptixSessionState state =
                        EcliptixSessionState.Parser.ParseFrom(stateResult.Unwrap());
                    Result<bool, NetworkFailure> restoreResult =
                        await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!).ConfigureAwait(false);
                    restorationSuccessful = restoreResult.IsOk && restoreResult.Unwrap();
                }
                catch (Exception)
                {
                    restorationSuccessful = false;
                }
            }
            else
            {
                Result<EcliptixSessionState, NetworkFailure> newResult =
                    await EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);
                restorationSuccessful = newResult.IsOk;
            }

            if (restorationSuccessful)
            {
                ExitOutage();
                ResetRetryStrategyAfterOutage();
            }
            else
            {
                await PerformReconnectionLogic().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool ShouldAllowDuplicateRequests(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.InitiateVerification => true,
            RpcServiceType.ValidateMobileNumber => true,
            _ => false
        };
    }

    public void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                {
                    EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
                    EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
                    if (connection == null)
                        return;

                    if (connection.ExchangeType == PubKeyExchangeType.ServerStreaming)
                    {
                        return;
                    }

                    Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                    Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                    if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                    {
                        EcliptixSessionState state = new()
                        {
                            ConnectId = connectId,
                            IdentityKeys = idKeysStateResult.Unwrap(),
                            RatchetState = ratchetStateResult.Unwrap()
                        };

                        await PersistSessionStateAsync(state, connectId).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                        $"Failed to persist ratchet state for connection {connectId}: {ex.Message}");
                });
            }
        });
    }

    public void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                {
                    EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
                    EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
                    if (connection == null)
                        return;

                    if (connection.ExchangeType == PubKeyExchangeType.ServerStreaming)
                    {
                        return;
                    }

                    Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                    Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                    if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                    {
                        EcliptixSessionState state = new()
                        {
                            ConnectId = connectId,
                            IdentityKeys = idKeysStateResult.Unwrap(),
                            RatchetState = ratchetStateResult.Unwrap()
                        };

                        await PersistSessionStateAsync(state, connectId).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                        $"Failed to persist chain state for connection {connectId}: {ex.Message}");
                });
            }
        });
    }

    public void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys)
    {
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _shutdownCts.Cancel();

                foreach (KeyValuePair<string, CancellationTokenSource> kv in _inFlightRequests.ToArray())
                {
                    if (!_inFlightRequests.TryRemove(kv.Key, out CancellationTokenSource? cts)) continue;
                    try
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                foreach (KeyValuePair<uint, CancellationTokenSource> kv in _activeStreams.ToArray())
                {
                    if (!_activeStreams.TryRemove(kv.Key, out CancellationTokenSource? streamCts)) continue;
                    try
                    {
                        streamCts.Cancel();
                        streamCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                lock (_outageLock)
                {
                    _outageRecoveredTcs.TrySetException(new OperationCanceledException("Provider shutting down"));
                }

                List<KeyValuePair<uint, EcliptixProtocolSystem>> connectionsToDispose = new(_connections);
                _connections.Clear();

                foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in connectionsToDispose)
                {
                    try
                    {
                        connection.Value?.Dispose();
                    }
                    catch (Exception)
                    {
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

                _shutdownCts.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }

    public async Task<Result<Unit, NetworkFailure>> ForceFreshConnectionAsync()
    {
        _retryStrategy.ClearExhaustedOperations();

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
            NetworkConstants.ErrorMessages.SessionNotFoundOnServer,
            failOnMissingState: true).ConfigureAwait(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformRecoveryWithStateRestorationAsync(
        RestoreRetryMode retryMode,
        string failureMessage,
        bool failOnMissingState = false)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        _connections.TryRemove(connectId, out _);

        byte[]? membershipId = GetMembershipIdBytes();
        if (membershipId == null)
        {
            Log.Warning("[CLIENT-RECOVERY-STATE] Cannot load state: membershipId not available. ConnectId: {ConnectId}",
                connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("MembershipId not available for state restoration"));
        }

        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);

        if (stateResult.IsErr)
        {
            if (failOnMissingState)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("No stored state for immediate recovery"));
            }

            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(failureMessage));
        }

        bool restorationSucceeded = false;
        try
        {
            byte[] stateBytes = stateResult.Unwrap();
            EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);

            Result<bool, NetworkFailure> restoreResult =
                await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!, retryMode).ConfigureAwait(false);

            restorationSucceeded = restoreResult.IsOk && restoreResult.Unwrap();

            if (restorationSucceeded)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
                });
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

    private void ResetRetryStrategyAfterOutage()
    {
        _retryStrategy.ResetConnectionState();

        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in _connections)
        {
            _retryStrategy.MarkConnectionHealthy(connection.Key);
        }
    }

    private byte[]? GetMembershipIdBytes()
    {
        if (!_applicationInstanceSettings.HasValue) return null;
        ApplicationInstanceSettings settings = _applicationInstanceSettings.Value!;
        if (settings.Membership == null) return null;
        return settings.Membership.UniqueIdentifier.ToByteArray();
    }

    private async Task PersistSessionStateAsync(EcliptixSessionState state, uint connectId, byte[]? membershipIdOverride = null)
    {
        byte[]? membershipId = membershipIdOverride ?? GetMembershipIdBytes();
        if (membershipId == null)
        {
            Log.Warning("[CLIENT-STATE-PERSIST] Cannot persist: membershipId not available. ConnectId: {ConnectId}",
                connectId);
            return;
        }

        await SecureByteStringInterop.WithByteStringAsSpan(
            state.ToByteString(),
            span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(), membershipId)).ConfigureAwait(false);

        string timestampKey = $"{connectId}_timestamp";
        await _applicationSecureStorageProvider.StoreAsync(timestampKey,
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary())).ConfigureAwait(false);
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        return _connections.ContainsKey(connectId);
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
                    Log.Warning("[CLIENT-TRY-RESTORE] Cannot restore: membershipId not available. ConnectId: {ConnectId}",
                        connectId);
                    return Result<bool, NetworkFailure>.Ok(false);
                }

                Result<byte[], SecureStorageFailure> stateResult =
                    await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId).ConfigureAwait(false);
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

            string masterKeyFingerprint = Convert.ToHexString(SHA256.HashData(masterKeyBytes))[..16];
            Log.Information("[CLIENT-AUTH-MASTERKEY] Using master key to create identity keys. ConnectId: {ConnectId}, MembershipId: {MembershipId}, MasterKeyFingerprint: {MasterKeyFingerprint}",
                connectId, membershipId, masterKeyFingerprint);

            string masterKeyHashForLog = Convert.ToHexString(SHA256.HashData(masterKeyBytes));
            Log.Information("[CLIENT-AUTH-HKDF-PARAMS] HKDF parameters for initial root key. ConnectId: {ConnectId}, IKM (master key hash): {IkmHash}, Salt: NULL, Info: 'ecliptix-protocol-root-key'",
                connectId, masterKeyHashForLog);

            rootKeyBytes = new byte[32];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: masterKeyBytes,
                output: rootKeyBytes,
                salt: null,
                info: "ecliptix-protocol-root-key"u8.ToArray()
            );

            string rootKeyHash = Convert.ToHexString(SHA256.HashData(rootKeyBytes))[..16];
            Log.Information("[CLIENT-AUTH-ROOTKEY] Derived root key from master key using HKDF. ConnectId: {ConnectId}, RootKeyHash: {RootKeyHash}",
                connectId, rootKeyHash);
            Log.Information("[CLIENT-AUTH-ROOTKEY-BEFORE-COMPLETE] Initial root key before CompleteAuthenticatedPubKeyExchange. ConnectId: {ConnectId}, InitialRootKeyHash: {RootKeyHash}",
                connectId, rootKeyHash);

            Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> identityKeysResult =
                EcliptixSystemIdentityKeys.CreateFromMasterKey(masterKeyBytes, membershipId, NetworkConstants.Protocol.DefaultOneTimeKeyCount);

            if (identityKeysResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(
                    identityKeysResult.UnwrapErr().ToNetworkFailure());
            }

            EcliptixSystemIdentityKeys identityKeys = identityKeysResult.Unwrap();

            string identityX25519Hash = Convert.ToHexString(SHA256.HashData(identityKeys.IdentityX25519PublicKey))[..16];
            Log.Information("[CLIENT-AUTH-IDENTITY] Identity keys created. ConnectId: {ConnectId}, IdentityX25519Hash: {IdentityX25519Hash}",
                connectId, identityX25519Hash);

            if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? oldProtocol))
            {
                CancelOperationsForConnection(connectId);
                oldProtocol?.Dispose();
            }

            PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect;
            EcliptixProtocolSystem? newProtocol = new(identityKeys);

            try
            {
                newProtocol.SetEventHandler(this);

                Result<PubKeyExchange, EcliptixProtocolFailure> clientExchangeResult =
                    newProtocol.BeginDataCenterPubKeyExchange(connectId, exchangeType);

                if (clientExchangeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    return Result<Unit, NetworkFailure>.Err(
                        clientExchangeResult.UnwrapErr().ToNetworkFailure());
                }

                PubKeyExchange clientExchange = clientExchangeResult.Unwrap();

                Log.Information("[CLIENT-AUTH-HANDSHAKE-SEND] Sending client pub key exchange. ConnectId: {ConnectId}, InitialDhPublicKey: {InitialDhKeyHash}",
                    connectId, clientExchange.InitialDhPublicKey.IsEmpty ? "NONE" : Convert.ToHexString(clientExchange.InitialDhPublicKey.ToByteArray())[..Math.Min(16, clientExchange.InitialDhPublicKey.Length * 2)]);

                string clientDhFullHash = Convert.ToHexString(SHA256.HashData(clientExchange.InitialDhPublicKey.ToByteArray()));
                Log.Information("[CLIENT-AUTH-DH-CLIENT] Client DH public key full hash. ConnectId: {ConnectId}, ClientDhHash: {DhHash}",
                    connectId, clientDhFullHash);

                AuthenticatedEstablishRequest authenticatedRequest = new()
                {
                    MembershipUniqueId = membershipIdentifier,
                    ClientPubKeyExchange = clientExchange
                };

                Result<SecureEnvelope, NetworkFailure> serverResponseResult =
                    await _rpcServiceManager.AuthenticatedEstablishSecureChannelAsync(
                        _networkEvents, _systemEvents, authenticatedRequest).ConfigureAwait(false);

                if (serverResponseResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                            $"Failed to establish authenticated channel: {serverResponseResult.UnwrapErr().Message}");
                    });
                    return Result<Unit, NetworkFailure>.Err(serverResponseResult.UnwrapErr());
                }

                SecureEnvelope responseEnvelope = serverResponseResult.Unwrap();

                CertificatePinningService? certificatePinningService =
                    _certificatePinningServiceFactory.GetOrInitializeService();

                if (certificatePinningService == null)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                            "Failed to initialize certificate pinning service");
                    });
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
                }

                byte[] combinedEncryptedResponse = responseEnvelope.EncryptedPayload.ToByteArray();
                Result<byte[], NetworkFailure> decryptResult =
                    _rsaChunkEncryptor.DecryptInChunks(certificatePinningService, combinedEncryptedResponse);

                if (decryptResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                            $"Failed to decrypt server response: {decryptResult.UnwrapErr().Message}");
                    });
                    return Result<Unit, NetworkFailure>.Err(decryptResult.UnwrapErr());
                }

                PubKeyExchange serverExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

                Log.Information("[CLIENT-AUTH-HANDSHAKE-RECV] Received server pub key exchange. ConnectId: {ConnectId}, InitialDhPublicKey: {InitialDhKeyHash}",
                    connectId, serverExchange.InitialDhPublicKey.IsEmpty ? "NONE" : Convert.ToHexString(serverExchange.InitialDhPublicKey.ToByteArray())[..Math.Min(16, serverExchange.InitialDhPublicKey.Length * 2)]);

                string serverDhFullHash = Convert.ToHexString(SHA256.HashData(serverExchange.InitialDhPublicKey.ToByteArray()));
                Log.Information("[CLIENT-AUTH-DH-SERVER] Server DH public key received full hash. ConnectId: {ConnectId}, ServerDhHash: {DhHash}",
                    connectId, serverDhFullHash);

                Log.Information("[CLIENT-AUTH-FINALIZE] Finalizing protocol with HKDF root key. ConnectId: {ConnectId}",
                    connectId);

                Result<Unit, EcliptixProtocolFailure> completeResult =
                    newProtocol.CompleteAuthenticatedPubKeyExchange(serverExchange, rootKeyBytes);

                if (completeResult.IsErr)
                {
                    await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _systemEvents.NotifySystemStateAsync(SystemState.Busy,
                            $"Failed to complete authenticated handshake: {completeResult.UnwrapErr().Message}");
                    });
                    return Result<Unit, NetworkFailure>.Err(
                        completeResult.UnwrapErr().ToNetworkFailure());
                }

                EcliptixProtocolConnection? tempConn = newProtocol.GetConnection();
                if (tempConn != null)
                {
                    Result<RatchetState, EcliptixProtocolFailure> tempStateResult = tempConn.ToProtoState();
                    if (tempStateResult.IsOk)
                    {
                        string finalRootKeyHash = Convert.ToHexString(SHA256.HashData(tempStateResult.Unwrap().RootKey.ToByteArray()))[..16];
                        Log.Information("[CLIENT-AUTH-FINAL-ROOTKEY] Final root key after authenticated handshake complete. ConnectId: {ConnectId}, FinalRootKeyHash: {FinalRootKeyHash}",
                            connectId, finalRootKeyHash);
                    }
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

                        string sessionRootKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sessionState.RatchetState.RootKey.ToByteArray()))[..16];
                        string sendingChainKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sessionState.RatchetState.SendingStep.ChainKey.ToByteArray()))[..16];
                        string receivingChainKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(sessionState.RatchetState.ReceivingStep.ChainKey.ToByteArray()))[..16];

                        Log.Information("[CLIENT-AUTH-PROTOCOL-STATE] Protocol state created. ConnectId: {ConnectId}, RootKeyHash: {RootKeyHash}, SendingChainKeyHash: {SendingChainKeyHash}, ReceivingChainKeyHash: {ReceivingChainKeyHash}",
                            connectId, sessionRootKeyHash, sendingChainKeyHash, receivingChainKeyHash);

                        await PersistSessionStateAsync(sessionState, connectId, membershipIdentifier.ToByteArray()).ConfigureAwait(false);

                        Log.Information("[CLIENT-AUTH-SAVED] Authenticated protocol state saved. ConnectId: {ConnectId}, Sending: {Sending}, Receiving: {Receiving}, MembershipId: {MembershipId}",
                            connectId, sessionState.RatchetState.SendingStep.CurrentIndex, sessionState.RatchetState.ReceivingStep.CurrentIndex, membershipIdentifier);
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
        try
        {
            await _secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
