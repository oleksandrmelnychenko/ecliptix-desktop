using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Core.Network.RpcServices;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Security;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network.Providers;

/// <summary>
/// Adapter to connect protocol events to state persistence callbacks
/// </summary>
public class ProtocolEventAdapter : IProtocolEventHandler
{
    private readonly IProtocolStateCallbacks? _stateCallbacks;
    
    public ProtocolEventAdapter(IProtocolStateCallbacks? stateCallbacks)
    {
        _stateCallbacks = stateCallbacks;
    }
    
    public void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex)
    {
        if (_stateCallbacks != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _stateCallbacks.OnDhRatchetPerformed(connectId, isSending, newIndex);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in DH ratchet callback");
                }
            });
        }
    }
    
    public void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength)
    {
        if (_stateCallbacks != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _stateCallbacks.OnChainSynchronized(connectId, localLength, remoteLength);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in chain sync callback");
                }
            });
        }
    }
    
    public void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys)
    {
        if (_stateCallbacks != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _stateCallbacks.OnMessageReceived(connectId, messageIndex, hasSkippedKeys);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in message processed callback");
                }
            });
        }
    }
}

public sealed class NetworkProvider(
    RpcServiceManager rpcServiceManager,
    ISecureStorageProvider secureStorageProvider,
    IRpcMetaDataProvider rpcMetaDataProvider,
    INetworkEvents networkEvents,
    ISystemEvents systemEvents) : INetworkProvider
{
    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    private const int DefaultOneTimeKeyCount = 5;

    private static readonly SemaphoreSlim SecrecyChannelRecoveryLock = new(1, 1);
    private volatile bool _isSecrecyChannelConsideredHealthy;
    
    private ProtocolStatePersistence? _statePersistence;
    private IProtocolStateCallbacks? _stateCallbacks;
    private ProtocolEventAdapter? _eventAdapter;

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    public ApplicationInstanceSettings ApplicationInstanceSettings =>
        _applicationInstanceSettings.Value!;

    public EcliptixProtocolSystem? GetProtocolSystem(uint connectId)
    {
        return _connections.TryGetValue(connectId, out var system) ? system : null;
    }
    
    public void InitializeStatePersistence(SecureStateStorage secureStorage)
    {
        _statePersistence = new ProtocolStatePersistence(secureStorage);
        _stateCallbacks = new ProtocolStateCallbacks(
            _statePersistence,
            GetProtocolSystem,
            () => _applicationInstanceSettings.HasValue 
                ? _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8() 
                : "unknown");
        _eventAdapter = new ProtocolEventAdapter(_stateCallbacks);
        
        foreach (KeyValuePair<uint, EcliptixProtocolSystem> kvp in _connections)
        {
            kvp.Value.SetEventHandler(_eventAdapter);
        }
        
        Log.Information("Protocol state persistence initialized");
    }
    
    public void ClearConnection(uint connectId)
    {
        if (_connections.TryRemove(connectId, out var system))
        {
            system?.Dispose();
            Log.Information("Cleared connection {ConnectId} from cache", connectId);
        }
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(DefaultOneTimeKeyCount).Unwrap();
        EcliptixProtocolSystem protocolSystem = new(identityKeys);
        
        if (_eventAdapter != null)
        {
            protocolSystem.SetEventHandler(_eventAdapter);
        }

        _connections.TryAdd(connectId, protocolSystem);

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);
        string culture = applicationInstanceSettings.Culture;

        rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void SetSecrecyChannelAsUnhealthy()
    {
        _isSecrecyChannelConsideredHealthy = false;
        networkEvents.InitiateChangeState(NetworkStatusChangedEvent.New(NetworkStatus.DataCenterDisconnected));
    }

    public async Task<Result<Unit, NetworkFailure>> RestoreSecrecyChannelAsync()
    {
        await SecrecyChannelRecoveryLock.WaitAsync();
        try
        {
            if (_isSecrecyChannelConsideredHealthy)
            {
                Log.Information("Session was already recovered by another thread. Skipping redundant recovery");
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            Log.Information("Starting session recovery process...");
            Result<Unit, NetworkFailure> result = await PerformFullRecoveryLogic();

            _isSecrecyChannelConsideredHealthy = result.IsOk;
            if (result.IsErr)
            {
                Log.Error(result.UnwrapErr().Message, "Session recovery failed.");
            }

            return result;
        }
        finally
        {
            SecrecyChannelRecoveryLock.Release();
        }
    }

    private async Task<Result<Unit, NetworkFailure>> PerformFullRecoveryLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);
        Result<Option<byte[]>, InternalServiceApiFailure> storedStateResult =
            await secureStorageProvider.TryGetByKeyAsync(connectId.ToString());
        if (storedStateResult.IsOk && storedStateResult.Unwrap().HasValue)
        {
            EcliptixSecrecyChannelState? state =
                EcliptixSecrecyChannelState.Parser.ParseFrom(storedStateResult.Unwrap().Value);
            Result<bool, NetworkFailure> restoreResult =
                await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!);
            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                Log.Information("Session successfully restored from storage");
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            Log.Warning("Failed to restore session from storage, falling back to reconnection");
        }

        Result<Unit, NetworkFailure> reconnectionResult = await PerformReconnectionLogic();
        if (reconnectionResult.IsErr)
        {
            return reconnectionResult;
        }

        Log.Information("Session successfully re-established via reconnection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType) =>
        Helpers.ComputeUniqueConnectId(
            applicationInstanceSettings.AppInstanceId.Span,
            applicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);

    public async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestAsync(
        uint connectId,
        RcpServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token = default)
    {
        return await RetryExecute(
            retryAttempt => TimeSpan.FromSeconds(retryAttempt * 6),
            shouldRetry: result => result.IsErr && result.UnwrapErr().Message.Contains("Secure session with"),
            onRetryAsync: async (attempt, result) =>
            {
                if (serviceType == RcpServiceType.RegisterAppDevice)
                    await EstablishConnectionOnly(connectId);
                else
                    await PerformReconnectionLogic();
            },
            block: async () =>
            {
                if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                {
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType("Connection not found"));
                }

                ServiceRequest request = BuildRequest(protocolSystem, serviceType, plainBuffer, flowType, connectId);
                return await SendRequestAsync(protocolSystem, request, onCompleted, token, connectId);
            });
    }

    public async static Task<TResult> RetryExecute<TResult>(
        Func<int, TimeSpan> retryInterval,
        Func<TResult, bool> shouldRetry,
        Func<int, TResult, Task> onRetryAsync,
        Func<Task<TResult>> block,
        int? maxRetryCount = null
    )
    {
        int attempt = 1;
        while (true)
        {
            TResult result = await block();

            if (!shouldRetry(result) || (maxRetryCount.HasValue && attempt >= maxRetryCount.Value))
                return result;

            await onRetryAsync(attempt, result);
            await Task.Delay(retryInterval(attempt));

            attempt++;
        }

        throw new InvalidOperationException("Unreachable code in RetryExecute.");
    }


    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelAsync(
        EcliptixSecrecyChannelState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId), applicationInstanceSettings.Culture);

        RestoreSecrecyChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse>.New(
                ServiceFlowType.Single,
                RcpServiceType.RestoreSecrecyChannel,
                request);

        Result<RestoreSecrecyChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
            await rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(networkEvents, systemEvents, serviceRequest);

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());

        RestoreSecrecyChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreSecrecyChannelResponse.Types.RestoreStatus.SessionResumed)
        {
            Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> syncSecrecyChannelResult =
                SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            
            if (_stateCallbacks != null && syncSecrecyChannelResult.IsOk)
            {
                var connectId = ecliptixSecrecyChannelState.ConnectId;
                _ = Task.Run(async () =>
                {
                    await _stateCallbacks.OnSessionEstablished(connectId, true);
                });
            }

            return Result<bool, NetworkFailure>.Ok(true);
        }

        Log.Information("Session not found on server (status: {Status}), will establish new channel", response.Status);
        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSecrecyChannelState, NetworkFailure>> EstablishSecrecyChannelAsync(
        uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<EcliptixSecrecyChannelState, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection not found"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, PubKeyExchangeType.DataCenterEphemeralConnect);

        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> action =
            SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange>.New(
                ServiceFlowType.Single,
                RcpServiceType.EstablishSecrecyChannel,
                pubKeyExchangeRequest.Unwrap());

        Result<PubKeyExchange, NetworkFailure> establishAppDeviceSecrecyChannelResult =
            await rpcServiceManager.EstablishAppDeviceSecrecyChannelAsync(networkEvents, systemEvents, action);

        PubKeyExchange peerPubKeyExchange = establishAppDeviceSecrecyChannelResult.Unwrap();

        protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection connection = protocolSystem.GetConnection();

        Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> ecliptixSecrecyChannelStateResult =
            idKeys.ToProtoState()
                .AndThen(identityKeysProto => connection.ToProtoState()
                    .Map(ratchetStateProto => new EcliptixSecrecyChannelState
                    {
                        ConnectId = connectId,
                        IdentityKeys = identityKeysProto,
                        PeerHandshakeMessage = peerPubKeyExchange,
                        RatchetState = ratchetStateProto
                    })
                );

        return ecliptixSecrecyChannelStateResult.ToNetworkFailure();
    }

    private Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> SyncSecrecyChannel(
        EcliptixSecrecyChannelState currentState,
        RestoreSecrecyChannelResponse serverResponse)
    {
        Result<EcliptixProtocolSystem, EcliptixProtocolFailure> systemResult = RecreateSystemFromState(currentState);
        if (systemResult.IsErr)
        {
            return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Err(systemResult.UnwrapErr());
        }

        EcliptixProtocolSystem system = systemResult.Unwrap();
        
        if (_eventAdapter != null)
        {
            system.SetEventHandler(_eventAdapter);
        }
        
        EcliptixProtocolConnection connection = system.GetConnection();

        Result<Unit, EcliptixProtocolFailure> syncResult = connection.SyncWithRemoteState(
            serverResponse.SendingChainLength,
            serverResponse.ReceivingChainLength
        );

        if (syncResult.IsErr)
        {
            system.Dispose(); 
            return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Err(syncResult.UnwrapErr());
        }

        _connections.TryAdd(currentState.ConnectId, system);

        CreateStateFromSystem(currentState, system);

        return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Ok(currentState);
    }

    private static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> RecreateSystemFromState(
        EcliptixSecrecyChannelState state)
    {
        Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> idKeysResult =
            EcliptixSystemIdentityKeys.FromProtoState(state.IdentityKeys);
        if (idKeysResult.IsErr)
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> connResult =
            EcliptixProtocolConnection.FromProtoState(state.ConnectId, state.RatchetState);

        if (connResult.IsErr)
        {
            idKeysResult.Unwrap().Dispose();
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
        }

        return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap());
    }

    private static void CreateStateFromSystem(EcliptixSecrecyChannelState oldState, EcliptixProtocolSystem system)
    {
        system.GetConnection().ToProtoState().Map(newRatchetState =>
        {
            EcliptixSecrecyChannelState? newState = oldState.Clone();
            newState.RatchetState = newRatchetState;
            return newState;
        });
    }

    private async Task<Result<Unit, NetworkFailure>> PerformReconnectionLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Unit, NetworkFailure> establishResult = await EstablishConnectionOnly(connectId);

        if (establishResult.IsErr)
        {
            return establishResult;
        }


        Log.Information("Successfully reconnected and established new session");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
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

        Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSecrecyChannelState secrecyChannelState = establishResult.Unwrap();
        await secureStorageProvider.StoreAsync(connectId.ToString(), secrecyChannelState.ToByteArray());
        
        if (_stateCallbacks != null)
        {
            _ = Task.Run(async () =>
            {
                await _stateCallbacks.OnSessionEstablished(connectId, false);
            });
        }

        Log.Information("Successfully established new connection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private ServiceRequest BuildRequest(
        EcliptixProtocolSystem protocolSystem,
        RcpServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        uint connectId)
    {
        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);
        
        CipherPayload cipherPayload = outboundPayload.Unwrap();
        
        if (_stateCallbacks != null)
        {
            _ = Task.Run(async () =>
            {
                await _stateCallbacks.OnMessageSent(
                    connectId, 
                    cipherPayload.RatchetIndex, 
                    cipherPayload.DhPublicKey.Length > 0);
            });
        }

        return ServiceRequest.New(flowType, serviceType, cipherPayload, []);
    }

    private async Task<Result<Unit, NetworkFailure>> SendRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token,
        uint connectId = 0)
    {
        Console.WriteLine($"[DESKTOP] SendRequestAsync - ServiceType: {request.RcpServiceMethod}");
        Result<RpcFlow, NetworkFailure> invokeResult =
            await rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr) 
        {
            Console.WriteLine($"[DESKTOP] InvokeServiceRequestAsync failed: {invokeResult.UnwrapErr().Message}");
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                Result<CipherPayload, NetworkFailure> callResult = await singleCall.Result;
                if (callResult.IsErr) 
                {
                    Console.WriteLine($"[DESKTOP] RPC call failed: {callResult.UnwrapErr().Message}");
                    return Result<Unit, NetworkFailure>.Err(callResult.UnwrapErr());
                }

                CipherPayload inboundPayload = callResult.Unwrap();
                Console.WriteLine($"[DESKTOP] Received CipherPayload - Nonce: {Convert.ToHexString(inboundPayload.Nonce.ToByteArray())}, Size: {inboundPayload.Cipher.Length}");
                Result<byte[], EcliptixProtocolFailure> decryptedData =
                    protocolSystem.ProcessInboundMessage(inboundPayload);
                if (decryptedData.IsErr)
                {
                    Console.WriteLine($"[DESKTOP] Decryption failed: {decryptedData.UnwrapErr().Message}");
                    return Result<Unit, NetworkFailure>.Err(decryptedData.UnwrapErr().ToNetworkFailure());
                }

                Console.WriteLine($"[DESKTOP] Successfully decrypted response");
                
                if (_stateCallbacks != null && connectId > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await _stateCallbacks.OnMessageReceived(
                            connectId,
                            inboundPayload.RatchetIndex,
                            false); 
                    });
                }
                
                Result<Unit, NetworkFailure> callbackOutcome = await onCompleted(decryptedData.Unwrap());
                if (callbackOutcome.IsErr) return callbackOutcome;
                break;

            case RpcFlow.InboundStream inboundStream:
                await foreach (Result<CipherPayload, NetworkFailure> streamItem in
                               inboundStream.Stream.WithCancellation(token))
                {
                    if (streamItem.IsErr) continue;

                    CipherPayload streamPayload = streamItem.Unwrap();
                    Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                        protocolSystem.ProcessInboundMessage(streamPayload);
                    if (streamDecryptedData.IsErr) continue;

                    Result<Unit, NetworkFailure> streamCallbackOutcome =
                        await onCompleted(streamDecryptedData.Unwrap());
                    if (streamCallbackOutcome.IsErr)
                    {
                        Log.Warning("Stream callback failed: {Error}", streamCallbackOutcome.UnwrapErr());
                    }
                }

                break;

            case RpcFlow.OutboundSink _:
            case RpcFlow.BidirectionalStream _:
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Unsupported stream type"));
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }
}