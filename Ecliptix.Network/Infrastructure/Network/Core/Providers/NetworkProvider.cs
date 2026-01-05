using System.Security.Cryptography;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Infrastructure.Network.Core.Constants;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protocol.System.Interfaces;
using Ecliptix.Protocol.System.Native;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed partial class NetworkProvider : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly NetworkProviderDependencies _dependencies;
    private readonly NetworkProviderServices _services;
    private readonly NetworkProviderSecurity _security;
    private const string DEFAULT_CULTURE_CODE = "en-US";

    public NetworkProvider(
        NetworkProviderDependencies dependencies,
        NetworkProviderServices services,
        NetworkProviderSecurity security)
    {
        _dependencies = dependencies;
        _services = services;
        _security = security;
    }

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

    private static byte[] DeriveMasterKeyFingerprint(byte[] masterKey, Guid accountId)
    {
        const string fingerprintInfo = "ecliptix-master-key-fingerprint";
        byte[] infoBytes = System.Text.Encoding.UTF8.GetBytes($"{fingerprintInfo}:v1:{accountId}");
        using HMACSHA256 hmac = new(masterKey);
        return hmac.ComputeHash(infoBytes);
    }

    /// <summary>
    /// Process handshake response for authenticated channels. Uses externally derived root key
    /// instead of X3DH-derived key to match server's OPAQUE-based derivation.
    /// </summary>
    private Result<PubKeyExchange, NetworkFailure> ProcessAuthenticatedHandshakeResponse(
        SecureEnvelope responseEnvelope,
        CertificatePinningService certificatePinningService,
        uint connectId,
        byte[] rootKey)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult =
            _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        Result<byte[], NetworkFailure> decryptResult =
            _security.RsaChunkEncryptor.DecryptInChunks(certificatePinningService,
                responseEnvelope.EncryptedPayload.ToByteArray());
        if (decryptResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(decryptResult.UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

        // Use CompleteHandshake with OPAQUE-derived root key (matching server derivation)
        Result<Unit, EcliptixProtocolFailure> completeResult =
            nativeSessionResult.Unwrap().CompleteHandshake(peerPubKeyExchange.ToByteArray(), rootKey);
        return completeResult.IsErr
            ? Result<PubKeyExchange, NetworkFailure>.Err(completeResult.UnwrapErr().ToNetworkFailure())
            : Result<PubKeyExchange, NetworkFailure>.Ok(peerPubKeyExchange);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelInternalAsync(
        SecrecyChannelRequest request)
    {
        PublishConnectingEventIfNeeded(request.ExchangeType, request.ConnectId);

        Result<(SecureEnvelope Envelope, CertificatePinningService Service), NetworkFailure> prepareResult =
            await PrepareNativeHandshakeEnvelopeAsync(request).ConfigureAwait(false);

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
            ProcessNativeHandshakeResponse(establishResult.Unwrap(), certificatePinningService, request);

        if (processResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(processResult.UnwrapErr());
        }

        return await CreateAndPersistSessionStateAsync(
            request,
            processResult.Unwrap());
    }

    private void PublishConnectingEventIfNeeded(PubKeyExchangeType exchangeType, uint connectId)
    {
        if (exchangeType != PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            return;
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _services.ConnectivityService.PublishAsync(ConnectivityIntent.Connecting(connectId)).ContinueWith(
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
            return await _services.RetryStrategy.ExecuteRpcOperationAsync(
                (_, ct) => _dependencies.RpcServiceManager.EstablishSecrecyChannelAsync(
                    _services.ConnectivityService,
                    envelope,
                    request.ExchangeType,
                    cancellationToken: ct),
                operationName,
                request.ConnectId,
                serviceType: RpcServiceType.EstablishSecrecyChannel,
                maxRetries: request.MaxRetries.Value,
                cancellationToken: finalToken).ConfigureAwait(false);
        }

        return await _services.RetryStrategy.ExecuteRpcOperationAsync(
            (_, ct) => _dependencies.RpcServiceManager.EstablishSecrecyChannelAsync(
                _services.ConnectivityService,
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
        PubKeyExchange peerPubKeyExchange)
    {
        if (!ShouldPersistSessionState(request))
        {
            return Task.FromResult(
                Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None));
        }

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            CreateSessionState(request.ConnectId, peerPubKeyExchange);

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

        _services.PendingRequestManager.RemovePendingRequest(
            BuildSecrecyChannelPendingKey(request.ConnectId, request.ExchangeType));
        ExitOutage();

        return Task.FromResult(
            Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(
                Option<EcliptixSessionState>.Some(stateResult.Unwrap())));
    }

    private static bool ShouldPersistSessionState(SecrecyChannelRequest request) =>
        request is { SaveState: true, ExchangeType: PubKeyExchangeType.DataCenterEphemeralConnect };

    private Result<EcliptixSessionState, NetworkFailure> CreateSessionState(
        uint connectId,
        PubKeyExchange peerPubKeyExchange)
    {
        // Managed protocol is retired; persist only the native state plus handshake metadata.
        EcliptixSessionState state = new()
        {
            ConnectId = connectId,
            PeerHandshakeMessage = peerPubKeyExchange
        };
        if (_nativeSessions.Get(connectId).IsOk)
        {
            Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult = _nativeSessions.Get(connectId);
            if (nativeSessionResult.IsOk)
            {
                NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();
                Result<byte[], EcliptixProtocolFailure> exportResult = nativeSession.ExportState();
                if (exportResult.IsOk)
                {
                    state.NativeState = ByteString.CopyFrom(exportResult.Unwrap());
                    state.NativePeerBundle = peerPubKeyExchange.Payload;
                    state.NativeIsInitiator = peerPubKeyExchange.State ==
                                              PubKeyExchangeState.Init;
                    if (_applicationInstanceSettings.IsSome)
                    {
                        state.MembershipId =
                            _applicationInstanceSettings.Value!.Membership?.UniqueIdentifier.ToBase64() ??
                            string.Empty;
                    }
                }
            }
        }

        return Result<EcliptixSessionState, NetworkFailure>.Ok(state);
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

    public void SetServerPublicKey(ByteString serverPublicKey)
    {
        lock (_appInstanceSetterLock)
        {
            if (!_applicationInstanceSettings.IsSome)
            {
                return;
            }

            ApplicationInstanceSettings current = _applicationInstanceSettings.Value!;
            ApplicationInstanceSettings updated = current.Clone();
            updated.ServerPublicKey = serverPublicKey;
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(updated);
        }
    }

    public void SetServerKyberPublicKey(ByteString serverKyberPublicKey)
    {
        lock (_appInstanceSetterLock)
        {
            if (!_applicationInstanceSettings.IsSome)
            {
                return;
            }

            ApplicationInstanceSettings current = _applicationInstanceSettings.Value!;
            ApplicationInstanceSettings updated = current.Clone();
            updated.ServerKyberPublicKey = serverKyberPublicKey;
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(updated);
        }
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        EnsureNativeInitialized();
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> identityResult =
            NativeProtocolSystem.CreateIdentity();
        if (identityResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native identity: {identityResult.UnwrapErr().Message}");
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeCreateResult =
            _nativeSessions.CreateOrReplace(connectId, identityResult.Unwrap(), this.OnProtocolStateChanged);
        if (nativeCreateResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native session: {nativeCreateResult.UnwrapErr().Message}");
        }

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);
        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? DEFAULT_CULTURE_CODE
            : applicationInstanceSettings.Culture;

        _dependencies.RpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    private void EnsureNativeInitialized()
    {
        lock (_nativeInitLock)
        {
            if (_nativeInitialized)
            {
                return;
            }

            Result<Unit, EcliptixProtocolFailure> initResult = NativeProtocolSystem.Initialize();
            if (initResult.IsErr)
            {
                throw new InvalidOperationException(
                    $"Failed to initialize native protocol: {initResult.UnwrapErr().Message}");
            }

            _nativeInitialized = true;
            if (!_nativeVersionLogged)
            {
                string version = NativeProtocolSystem.GetVersion();
                Log.Information("Native Ecliptix protocol initialized (version: {Version})", version);
                _nativeVersionLogged = true;
            }
        }
    }

    public void ClearConnection(uint connectId)
    {
        _nativeSessions.Remove(connectId);
    }

    public void ClearExhaustedOperations() => _services.RetryStrategy.ClearExhaustedOperations();

    public bool HasConnection(uint connectId) => _nativeSessions.Has(connectId);

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
            return await HandleRestoreFailureAsync(
                restoreResponse.UnwrapErr(),
                ecliptixSecrecyChannelState,
                applicationInstanceSettings,
                retryMode,
                enablePendingRegistration).ConfigureAwait(false);
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
            ? DEFAULT_CULTURE_CODE
            : settings.Culture;

        _dependencies.RpcMetaDataProvider.SetAppInfo(
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

        return await _services.RetryStrategy.ExecuteRpcOperationAsync(
            (_, ct) => _dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                _services.ConnectivityService,
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

        return await _services.RetryStrategy.ExecuteManualRetryRpcOperationAsync(
            (_, ct) => _dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                _services.ConnectivityService,
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
            return await _dependencies.RpcServiceManager.RestoreSecrecyChannelAsync(
                _services.ConnectivityService,
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

    private async Task<Result<bool, NetworkFailure>> HandleRestoreFailureAsync(
        NetworkFailure failure,
        EcliptixSessionState sessionState,
        ApplicationInstanceSettings settings,
        RestoreRetryMode retryMode,
        bool enablePendingRegistration)
    {
        if (FailureClassification.IsProtocolStateMismatch(failure))
        {
            Log.Warning(
                "[NETWORK-PROVIDER] Protocol state mismatch during restore. Cleaning up stale state. ConnectId: {ConnectId}",
                sessionState.ConnectId);

            await CleanupFailedAuthenticationAsync(sessionState.ConnectId).ConfigureAwait(false);
        }

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
                await HandleSessionRestoredAsync(response, sessionState, enablePendingRegistration).ConfigureAwait(false),
            RestoreChannelResponse.Types.Status.SessionNotFound =>
                await HandleSessionNotFoundAsync(sessionState.ConnectId),
            _ => Result<bool, NetworkFailure>.Ok(false)
        };
    }

    private async Task<Result<bool, NetworkFailure>> HandleSessionRestoredAsync(
        RestoreChannelResponse response,
        EcliptixSessionState sessionState,
        bool enablePendingRegistration)
    {
        Result<Unit, EcliptixProtocolFailure> syncResult =
            await SyncSecrecyChannelAsync(sessionState, response).ConfigureAwait(false);

        if (syncResult.IsErr)
        {
            EcliptixProtocolFailure error = syncResult.UnwrapErr();
            bool isValidationFailure =
                error.FailureType == EcliptixProtocolFailureType.STATE_MISMATCH ||
                error.Message.Contains("Session validation failed", StringComparison.OrdinalIgnoreCase);
            return isValidationFailure
                ? Result<bool, NetworkFailure>.Ok(false)
                : Result<bool, NetworkFailure>.Err(error.ToNetworkFailure());
        }

        if (enablePendingRegistration)
        {
            _services.PendingRequestManager.RemovePendingRequest(
                BuildSecrecyChannelRestoreKey(sessionState.ConnectId));
        }

        PersistProtocolStateInBackground(sessionState.ConnectId);

        ExitOutage();
        return Result<bool, NetworkFailure>.Ok(true);
    }

    private async Task<Result<bool, NetworkFailure>> HandleSessionNotFoundAsync(uint connectId)
    {
        Log.Information(
            "[NETWORK-PROVIDER] Session not found on server. Cleaning up stale state. ConnectId: {ConnectId}",
            connectId);

        await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);

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

        _nativeSessions.Remove(connectId);
        InitiateEcliptixProtocolSystemForType(connectId);

        Result<Option<EcliptixSessionState>, NetworkFailure> establishOptionResult =
            await EstablishSecrecyChannelForTypeAsync(connectId, exchangeType).ConfigureAwait(false);

        if (!establishOptionResult.IsErr)
        {
            return Result<uint, NetworkFailure>.Ok(connectId);
        }

        _nativeSessions.Remove(connectId);
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

        _nativeSessions.Remove(connectId);
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private void InitiateEcliptixProtocolSystemForType(uint connectId)
    {
        Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> identityResult =
            NativeProtocolSystem.CreateIdentity();
        if (identityResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native identity: {identityResult.UnwrapErr().Message}");
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeCreateResult =
            _nativeSessions.CreateOrReplace(connectId, identityResult.Unwrap(), this.OnProtocolStateChanged);
        if (nativeCreateResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native session: {nativeCreateResult.UnwrapErr().Message}");
        }
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

    private async Task<Result<Unit, EcliptixProtocolFailure>> SyncSecrecyChannelAsync(
        EcliptixSessionState currentState,
        RestoreChannelResponse peerSecrecyChannelState)
    {
        Result<Unit, EcliptixProtocolFailure> restoreResult =
            await RestoreNativeSessionFromStateAsync(currentState).ConfigureAwait(false);
        if (restoreResult.IsErr)
        {
            return restoreResult;
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> sessionResult =
            _nativeSessions.Get(currentState.ConnectId);
        if (sessionResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(sessionResult.UnwrapErr());
        }

        Result<(uint SendingIndex, uint ReceivingIndex), EcliptixProtocolFailure> chainResult =
            sessionResult.Unwrap().GetChainIndices();
        if (chainResult.IsErr)
        {
            EcliptixProtocolFailure chainError = chainResult.UnwrapErr();
            if (chainError.Message.Contains("chain index", StringComparison.OrdinalIgnoreCase))
            {
                Log.Debug("[NETWORK-PROVIDER] Chain index validation unavailable: {Error}", chainError.Message);
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }

            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.StateMismatch(
                    $"Session validation failed: unable to read chain indices ({chainError.Message})."));
        }

        (uint localSending, uint localReceiving) = chainResult.Unwrap();
        // Server indices are from its perspective: server receiving == client sending, server sending == client receiving.
        uint serverReceiving = peerSecrecyChannelState.ReceivingChainLength;
        uint serverSending = peerSecrecyChannelState.SendingChainLength;

        if (localSending != serverReceiving || localReceiving != serverSending)
        {
            Log.Warning(
                "[NETWORK-PROVIDER] Session chain indices out of sync. ConnectId: {ConnectId}, " +
                "ClientSending: {ClientSending}, ClientReceiving: {ClientReceiving}, " +
                "ServerReceiving: {ServerReceiving}, ServerSending: {ServerSending}",
                currentState.ConnectId,
                localSending,
                localReceiving,
                serverReceiving,
                serverSending);

            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.StateMismatch(
                    $"Session validation failed: chain indices out of sync (client send {localSending}/recv {localReceiving}, " +
                    $"server recv {serverReceiving}/send {serverSending})."));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static bool TryResolveMembershipGuid(string membershipId, out Guid membershipGuid)
    {
        if (Guid.TryParse(membershipId, out membershipGuid))
        {
            return true;
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(membershipId);
            membershipGuid = Helpers.FromByteStringToGuid(ByteString.CopyFrom(decoded));
            return true;
        }
        catch
        {
            membershipGuid = Guid.Empty;
            return false;
        }
    }

    private async Task<Result<Unit, EcliptixProtocolFailure>> RestoreNativeSessionFromStateAsync(
        EcliptixSessionState state)
    {
        if (state.NativeState.Length == 0)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Missing native state for restoration"));
        }

        bool resolvedFromState = TryResolveMembershipGuid(state.MembershipId, out Guid membershipGuid);
        if (!resolvedFromState)
        {
            if (_applicationInstanceSettings.IsSome &&
                _applicationInstanceSettings.Value!.Membership?.UniqueIdentifier != null)
            {
                membershipGuid =
                    Helpers.FromByteStringToGuid(_applicationInstanceSettings.Value!.Membership!.UniqueIdentifier);
            }
            else
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput("Invalid membership identifier for restoration"));
            }
        }

        if (resolvedFromState &&
            _applicationInstanceSettings.IsSome &&
            _applicationInstanceSettings.Value!.Membership?.UniqueIdentifier != null)
        {
            Guid expectedMembershipId =
                Helpers.FromByteStringToGuid(_applicationInstanceSettings.Value!.Membership!.UniqueIdentifier);
            if (expectedMembershipId != membershipGuid)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput("Membership identifier mismatch for restoration"));
            }
        }

        Guid accountGuid;
        if (!state.AccountId.IsEmpty)
        {
            accountGuid = Helpers.FromByteStringToGuid(state.AccountId);
        }
        else if (_applicationInstanceSettings.IsSome &&
                 _applicationInstanceSettings.Value!.CurrentAccountId != null &&
                 !_applicationInstanceSettings.Value!.CurrentAccountId.IsEmpty)
        {
            accountGuid = Helpers.FromByteStringToGuid(_applicationInstanceSettings.Value!.CurrentAccountId);
        }
        else
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Account identifier missing for restoration"));
        }

        Result<SodiumSecureMemoryHandle, AuthenticationFailure> masterKeyResult =
            await _dependencies.IdentityService.LoadMasterKeyHandleAsync(accountGuid.ToString())
                .ConfigureAwait(false);

        if (masterKeyResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Missing master key for restoration: {masterKeyResult.UnwrapErr().Message}"));
        }

        byte[]? masterKeyBytes = null;
        using SodiumSecureMemoryHandle masterKeyHandle = masterKeyResult.Unwrap();

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput(
                        $"Failed to read master key for restoration: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            byte[] nativeStateBytes = state.NativeState.ToByteArray();
            Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeIdentityResult =
                NativeProtocolSystem.CreateIdentityFromSeed(masterKeyBytes, accountGuid.ToString());
            if (nativeIdentityResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(nativeIdentityResult.UnwrapErr());
            }

            EcliptixIdentityKeysWrapper nativeIdentity = nativeIdentityResult.Unwrap();
            Result<NativeProtocolSession, EcliptixProtocolFailure> nativeImportResult =
                _nativeSessions.CreateOrReplaceFromState(
                    state.ConnectId,
                    nativeIdentity,
                    nativeStateBytes,
                    this.OnProtocolStateChanged);
            if (nativeImportResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(nativeImportResult.UnwrapErr());
            }

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }
        }
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

    private Result<SecureEnvelope, NetworkFailure> EncryptPayload(
        uint connectId,
        byte[] plainBuffer)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult =
            _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsErr)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();
        Result<bool, EcliptixProtocolFailure> hasConnResult = nativeSession.HasConnection();
        if (hasConnResult.IsErr)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                hasConnResult.UnwrapErr().ToNetworkFailure());
        }

        if (!hasConnResult.Unwrap())
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Native protocol session not established"));
        }

        Result<byte[], EcliptixProtocolFailure> nativeCipher = nativeSession.SendMessage(plainBuffer);
        if (nativeCipher.IsErr)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                nativeCipher.UnwrapErr().ToNetworkFailure());
        }

        try
        {
            SecureEnvelope envelope = SecureEnvelope.Parser.ParseFrom(nativeCipher.Unwrap());
            return Result<SecureEnvelope, NetworkFailure>.Ok(envelope);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Failed to parse native envelope: {ex.Message}"));
        }
    }

    private Result<ServiceRequest, NetworkFailure> BuildRequestWithId(
        uint connectId,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        RpcRequestContext requestContext)
    {
        Result<SecureEnvelope, NetworkFailure> encryptResult = EncryptPayload(connectId, plainBuffer);

        if (encryptResult.IsErr)
        {
            return Result<ServiceRequest, NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        SecureEnvelope cipherPayload = encryptResult.Unwrap();

        return Result<ServiceRequest, NetworkFailure>.Ok(
            ServiceRequest.New(logicalOperationId, flowType, serviceType, cipherPayload, [], requestContext));
    }

    private Result<byte[], NetworkFailure> DecryptPayload(
        uint connectId,
        SecureEnvelope envelope)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult = _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();
        Result<bool, EcliptixProtocolFailure> hasConnResult = nativeSession.HasConnection();
        if (hasConnResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(hasConnResult.UnwrapErr().ToNetworkFailure());
        }

        if (!hasConnResult.Unwrap())
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Native protocol session not established"));
        }

        byte[] serializedEnvelope = envelope.ToByteArray();
        Result<Unit, EcliptixProtocolFailure> validateResult =
            nativeSession.ValidateEnvelopeHybridRequirements(serializedEnvelope);
        if (validateResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(validateResult.UnwrapErr().ToNetworkFailure());
        }

        Result<byte[], EcliptixProtocolFailure> decryptResult =
            nativeSession.ReceiveMessage(serializedEnvelope);
        if (decryptResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(decryptResult.UnwrapErr().ToNetworkFailure());
        }

        return Result<byte[], NetworkFailure>.Ok(decryptResult.Unwrap());
    }

    private void PersistProtocolStateInBackground(uint connectId)
    {
        Task persistTask = Task.Run(async () =>
        {
            try
            {
                await TryPersistProtocolStateAsync(connectId).ConfigureAwait(false);
            }
            finally
            {
                _pendingPersistTasks.TryRemove(connectId, out _);
            }
        });

        _pendingPersistTasks.AddOrUpdate(connectId, persistTask, (_, __) => persistTask);
    }

    private async Task TryPersistProtocolStateAsync(uint connectId)
    {
        if (_disposed || _shutdownCancellationToken.IsCancellationRequested)
        {
            return;
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeResult = _nativeSessions.Get(connectId);
        if (nativeResult.IsErr)
        {
            return;
        }

        Result<byte[], EcliptixProtocolFailure> exportResult = nativeResult.Unwrap().ExportState();
        if (exportResult.IsErr)
        {
            return;
        }

        byte[]? membershipId = GetMembershipIdBytes();
        if (membershipId == null)
        {
            return;
        }

        EcliptixSessionState? existingState = await TryLoadStoredStateAsync(connectId, membershipId)
            .ConfigureAwait(false);
        if (existingState == null)
        {
            Log.Warning(
                "[CLIENT-STATE-PERSIST] Skipping state update because existing state is missing. ConnectId: {ConnectId}",
                connectId);
            return;
        }

        EcliptixSessionState state = existingState.Clone();
        state.ConnectId = connectId;
        state.NativeState = ByteString.CopyFrom(exportResult.Unwrap());

        if (_applicationInstanceSettings.IsSome && string.IsNullOrWhiteSpace(state.MembershipId))
        {
            state.MembershipId = _applicationInstanceSettings.Value!.Membership?.UniqueIdentifier.ToBase64() ??
                                 string.Empty;
        }

        if (_applicationInstanceSettings.IsSome &&
            _applicationInstanceSettings.Value!.CurrentAccountId != null &&
            _applicationInstanceSettings.Value!.CurrentAccountId.Length > 0 &&
            state.AccountId.IsEmpty)
        {
            state.AccountId = _applicationInstanceSettings.Value!.CurrentAccountId;
        }

        await PersistSessionStateAsync(state, connectId, membershipId).ConfigureAwait(false);
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
                WaitForPendingPersistTasks();
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

    private void WaitForPendingPersistTasks()
    {
        Task[] pendingTasks = _pendingPersistTasks.Values.ToArray();

        if (pendingTasks.Length == 0)
        {
            return;
        }

        Log.Information(
            "[NETWORK-PROVIDER] Waiting for {Count} pending protocol state persistence task(s) to complete before shutdown",
            pendingTasks.Length);

        try
        {
            Task.WaitAll(pendingTasks, TimeSpan.FromSeconds(5));
            Log.Information("[NETWORK-PROVIDER] All pending protocol state persistence tasks completed successfully");
        }
        catch (AggregateException ex)
        {
            Log.Warning(ex,
                "[NETWORK-PROVIDER] Some protocol state persistence tasks failed or timed out during shutdown");
        }
        finally
        {
            _pendingPersistTasks.Clear();
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
        _nativeSessions.Dispose();
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

    private async Task RetryPendingRequestsAfterRecovery()
    {
        bool acquired = false;
        try
        {
            await _retryPendingRequestsGate.WaitAsync(_shutdownCancellationToken.Token).ConfigureAwait(false);
            acquired = true;
            await _services.PendingRequestManager.RetryAllPendingRequestsAsync().ConfigureAwait(false);
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

    public void ExitOutage()
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
            _services.ConnectivityService.PublishAsync(
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

            _services.ConnectivityService.PublishAsync(
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

    public async Task<Result<Unit, NetworkFailure>> ForceFreshConnectionAsync()
    {
        _services.RetryStrategy.ClearExhaustedOperations();

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
        _nativeSessions.Remove(connectId);

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
            await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
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

    private async Task<EcliptixSessionState?> TryLoadStoredStateAsync(uint connectId, byte[] membershipId)
    {
        Result<byte[], SecureStorageFailure> stateResult =
            await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
                .ConfigureAwait(false);

        if (stateResult.IsErr)
        {
            return null;
        }

        try
        {
            byte[] stateBytes = stateResult.Unwrap();
            return EcliptixSessionState.Parser.ParseFrom(stateBytes);
        }
        catch (InvalidProtocolBufferException ex)
        {
            Log.Warning(ex,
                "[CLIENT-STATE-PERSIST] Stored state is corrupted. ConnectId: {ConnectId}",
                connectId);
            return null;
        }
    }

    private void PublishConnectionRestored(uint connectId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _services.ConnectivityService.PublishAsync(
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
        foreach (uint connectionId in _nativeSessions.ActiveConnectionIds())
        {
            _services.RetryStrategy.MarkConnectionHealthy(connectionId);
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

        _services.PendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
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

        _services.PendingRequestManager.RegisterPendingRequest(pendingKey, async ct =>
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
                span => _dependencies.SecureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(),
                    membershipId))
            .ConfigureAwait(false);

        if (saveResult.IsErr)
        {
            Log.Warning("[CLIENT-STATE-PERSIST] Failed to save session state. ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, saveResult.UnwrapErr().Message);
        }

        string timestampKey = $"{connectId}_timestamp";
        await _dependencies.ApplicationSecureStorageProvider.StoreAsync(timestampKey,
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary())).ConfigureAwait(false);
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        return _nativeSessions.Get(connectId).IsOk;
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
                    _nativeSessions.Remove(connectId);

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
                    await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), membershipId)
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
        ByteString accountIdentifier,
        uint connectId)
    {
        RetryBehavior retryBehavior =
            _security.RetryPolicyProvider.GetRetryBehavior(RpcServiceType.EstablishAuthenticatedSecureChannel);
        Result<Unit, NetworkFailure> networkResult = await _services.RetryStrategy.ExecuteRpcOperationAsync(
            async (_, _) => await RecreateProtocolWithMasterKeyAsyncInternal(
                masterKeyHandle,
                membershipIdentifier,
                accountIdentifier,
                connectId).ConfigureAwait(false),
            "RecreateProtocolWithMasterKey",
            connectId,
            serviceType: RpcServiceType.EstablishAuthenticatedSecureChannel,
            maxRetries: retryBehavior.MaxAttempts - 1,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        // If server doesn't have master key shares (fresh server), fallback to fresh handshake
        if (networkResult.IsErr &&
            networkResult.UnwrapErr().FailureType == NetworkFailureType.MASTER_KEY_SHARES_NOT_FOUND)
        {
            Log.Warning("[RecreateProtocolWithMasterKey] Server missing master key shares, falling back to fresh handshake");
            Result<EcliptixSessionState, NetworkFailure> freshResult =
                await EstablishSecrecyChannelAsync(connectId).ConfigureAwait(false);

            if (freshResult.IsOk)
            {
                if (Volatile.Read(ref _outageState) == 1)
                {
                    ExitOutage();
                }
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            return Result<Unit, NetworkFailure>.Err(freshResult.UnwrapErr());
        }

        if (networkResult.IsOk && Volatile.Read(ref _outageState) == 1)
        {
            ExitOutage();
        }

        return networkResult;
    }

    private async Task<Result<Unit, NetworkFailure>> RecreateProtocolWithMasterKeyAsyncInternal(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier,
        ByteString accountIdentifier,
        uint connectId)
    {
        byte[]? masterKeyBytes = null;
        byte[]? masterKeyFingerprint = null;
        EcliptixIdentityKeysWrapper? nativeIdentity = null;

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

            if (accountIdentifier.IsEmpty)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Missing account identifier for authenticated protocol"));
            }

            Guid accountGuid = Helpers.FromByteStringToGuid(accountIdentifier);
            string accountId = accountGuid.ToString();
            masterKeyFingerprint = DeriveMasterKeyFingerprint(masterKeyBytes, accountGuid);

            Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeIdentityResult =
                NativeProtocolSystem.CreateIdentityFromSeed(masterKeyBytes, accountId);
            if (nativeIdentityResult.IsErr)
            {
                return Result<Unit, NetworkFailure>.Err(
                    nativeIdentityResult.UnwrapErr().ToNetworkFailure());
            }

            nativeIdentity = nativeIdentityResult.Unwrap();

            _nativeSessions.Remove(connectId);
            CancelOperationsForConnection(connectId);
            const PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect;
            Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult =
                _nativeSessions.CreateOrReplace(connectId, nativeIdentity, this.OnProtocolStateChanged);
            if (nativeSessionResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(
                    nativeSessionResult.UnwrapErr().ToNetworkFailure());
            }

            Result<byte[], EcliptixProtocolFailure> nativeHandshake =
                nativeSessionResult.Unwrap().BeginHandshake(connectId, (byte)exchangeType);
            if (nativeHandshake.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(
                    nativeHandshake.UnwrapErr().ToNetworkFailure());
            }

            // Parse the native output as PubKeyExchange (it already contains the full bundle with Kyber key)
            PubKeyExchange clientExchange = PubKeyExchange.Parser.ParseFrom(nativeHandshake.Unwrap());

            AuthenticatedEstablishRequest authenticatedRequest = new()
            {
                MembershipUniqueId = membershipIdentifier,
                AccountUniqueId = accountIdentifier,
                ClientPubKeyExchange = clientExchange.ToByteString(),
                MasterKeyFingerprint = ByteString.CopyFrom(masterKeyFingerprint)
            };

            Result<SecureEnvelope, NetworkFailure> serverResponseResult =
                await _dependencies.RpcServiceManager.EstablishAuthenticatedSecrecyChannelAsync(
                    _services.ConnectivityService, authenticatedRequest).ConfigureAwait(false);

            if (serverResponseResult.IsErr)
            {
                NetworkFailure failure = serverResponseResult.UnwrapErr();
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(failure);
            }

            Option<CertificatePinningService> certificatePinningService =
                await _security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

            if (!certificatePinningService.IsSome)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
            }

            // Derive root key from OPAQUE master key (matching server-side derivation)
            byte[] rootKey = DeriveRootKeyFromMasterKey(masterKeyBytes, accountGuid);

            Result<PubKeyExchange, NetworkFailure> processResult =
                ProcessAuthenticatedHandshakeResponse(
                    serverResponseResult.Unwrap(),
                    certificatePinningService.Value!,
                    connectId,
                    rootKey);

            // Wipe derived root key after use
            CryptographicOperations.ZeroMemory(rootKey);

            if (processResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(processResult.UnwrapErr());
            }

            Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionForPersist =
                _nativeSessions.Get(connectId);
            if (nativeSessionForPersist.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(nativeSessionForPersist.UnwrapErr().ToNetworkFailure());
            }

            NativeProtocolSession nativeSession = nativeSessionForPersist.Unwrap();
            Result<byte[], EcliptixProtocolFailure> nativeExport = nativeSession.ExportState();
            if (nativeExport.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(nativeExport.UnwrapErr().ToNetworkFailure());
            }

            // Persist native-only state plus seeds.
            EcliptixSessionState sessionState = new()
            {
                ConnectId = connectId,
                PeerHandshakeMessage = processResult.Unwrap(),
                NativeState = ByteString.CopyFrom(nativeExport.Unwrap()),
                NativePeerBundle = processResult.Unwrap().Payload,
                NativeIsInitiator = true,
                AccountId = accountIdentifier,
                MembershipId = _applicationInstanceSettings.IsSome
                    ? _applicationInstanceSettings.Value!.Membership?.UniqueIdentifier.ToBase64() ?? string.Empty
                    : string.Empty
            };

            await PersistSessionStateAsync(sessionState, connectId, membershipIdentifier.ToByteArray())
                .ConfigureAwait(false);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
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
            if (masterKeyFingerprint != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyFingerprint);
            }
        }
    }

    private async Task CleanupFailedAuthenticationAsync(uint connectId)
    {
        Result<Unit, SecureStorageFailure> deleteResult =
            await _dependencies.SecureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning(
                "[CLIENT-AUTH-CLEANUP] Failed to delete protocol state during authentication cleanup. ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }
    }
}
