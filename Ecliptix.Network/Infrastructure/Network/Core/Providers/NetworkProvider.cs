using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Infrastructure.Network.Core.Constants;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.SecureProtocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protected.Protocol.Interfaces;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Protected.Protocol.Utilities;
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

public sealed partial class NetworkProvider(
    NetworkProviderDependencies dependencies,
    NetworkProviderServices services,
    NetworkProviderSecurity security)
    : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly NetworkProviderDependencies _dependencies = dependencies;
    private readonly NetworkProviderServices _services = services;
    private readonly NetworkProviderSecurity _security = security;
    private const string DEFAULT_CULTURE_CODE = "en-US";
    private const int AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH = 32;

    private static readonly byte[] AuthenticatedEstablishProofContext =
        "Ecliptix.AuthenticatedEstablish.v1"u8.ToArray();

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
        byte[] infoBytes = Encoding.UTF8.GetBytes($"{fingerprintInfo}:v1:{accountId}");
        using HMACSHA256 hmac = new(masterKey);
        return hmac.ComputeHash(infoBytes);
    }

    private Result<Unit, NetworkFailure> ProcessAuthenticatedHandshakeResponse(
        SecureEnvelope responseEnvelope,
        CertificatePinningService certificatePinningService,
        uint connectId)
    {
        return ProcessNativeHandshakeResponse(responseEnvelope, certificatePinningService, connectId);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelInternalAsync(
        SecrecyChannelRequest request)
    {
        PublishConnectingEventIfNeeded(request.ExchangeType, request.ConnectId);

        Result<(SecureEnvelope Envelope, CertificatePinningService Service, byte[] HandshakeInit), NetworkFailure>
            prepareResult =
            await PrepareNativeHandshakeEnvelopeAsync(request).ConfigureAwait(false);

        if (prepareResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(prepareResult.UnwrapErr());
        }

        (SecureEnvelope envelope, CertificatePinningService certificatePinningService, byte[] handshakeInit) =
            prepareResult.Unwrap();

        Result<SecureEnvelope, NetworkFailure> establishResult =
            await ExecuteEstablishChannelRpcAsync(request, envelope);

        if (establishResult.IsErr)
        {
            return HandleEstablishChannelFailure(establishResult.UnwrapErr(), request);
        }

        Result<Unit, NetworkFailure> processResult =
            ProcessNativeHandshakeResponse(establishResult.Unwrap(), certificatePinningService, request.ConnectId);

        if (processResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(processResult.UnwrapErr());
        }

        Result<Option<EcliptixSessionState>, NetworkFailure> result =
            await CreateAndPersistSessionStateAsync(
                request,
                handshakeInit);

        if (result.IsOk)
        {
            PublishConnectedEvent(request.ConnectId);
        }

        return result;
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

    private void PublishConnectedEvent(uint connectId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _services.ConnectivityService.PublishAsync(ConnectivityIntent.Connected(connectId))
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        Log.Error(t.Exception, "[NETWORK-PROVIDER] Failed to publish Connected event");
                    }
                }, TaskScheduler.Default);
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
        _nativeSessions.ClearServerPreKeyBundle(request.ConnectId);
        Log.Debug("[HANDSHAKE-FAILURE] Cleared stale prekey bundle for connectId {ConnectId}, failure: {FailureType}",
            request.ConnectId, failure.FailureType);

        if (request.EnablePendingRegistration && ShouldQueueSecrecyChannelRetry(failure))
        {
            QueueSecrecyChannelEstablishRetry(request.ConnectId, request.ExchangeType, request.MaxRetries,
                request.SaveState);
        }

        return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(failure);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> CreateAndPersistSessionStateAsync(
        SecrecyChannelRequest request,
        byte[] handshakeInit)
    {
        if (!ShouldPersistSessionState(request))
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None);
        }

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            await CreateSessionStateAsync(
                request.ConnectId,
                handshakeInit,
                request.ExchangeType,
                ByteString.Empty,
                ByteString.Empty,
                null).ConfigureAwait(false);

        if (stateResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(stateResult.UnwrapErr());
        }

        if (!request.EnablePendingRegistration)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(
                    Option<EcliptixSessionState>.Some(stateResult.Unwrap()));
        }

        _services.PendingRequestManager.RemovePendingRequest(
            BuildSecrecyChannelPendingKey(request.ConnectId, request.ExchangeType));
        ExitOutage();

        return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(
                Option<EcliptixSessionState>.Some(stateResult.Unwrap()));
    }

    private static bool ShouldPersistSessionState(SecrecyChannelRequest request) =>
        request is { SaveState: true, ExchangeType: PubKeyExchangeType.DataCenterEphemeralConnect };

    private async Task<Result<EcliptixSessionState, NetworkFailure>> CreateSessionStateAsync(
        uint connectId,
        byte[] handshakeInit,
        PubKeyExchangeType exchangeType,
        ByteString membershipId,
        ByteString accountId,
        byte[]? identitySeed)
    {
        EcliptixSessionState state = new()
        {
            ConnectId = connectId,
            PeerHandshakeInit = ByteString.CopyFrom(handshakeInit),
            ExchangeType = exchangeType,
            MembershipId = membershipId,
            AccountId = accountId,
            IdentitySeed = identitySeed is { Length: > 0 } ? ByteString.CopyFrom(identitySeed) : ByteString.Empty
        };

        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult = _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsOk)
        {
            NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();
            byte[]? encryptionKey = null;
            try
            {
                encryptionKey = await _security.PlatformSecurityProvider.GetOrCreateSessionStateKeyAsync()
                    .ConfigureAwait(false);
                Result<byte[], EcliptixProtocolFailure> exportResult = nativeSession.ExportSealedState(encryptionKey);
                if (exportResult.IsOk)
                {
                    byte[] stateBytes = exportResult.Unwrap();
                    state.NativeState = ByteString.CopyFrom(stateBytes);
                }
            }
            finally
            {
                if (encryptionKey != null)
                {
                    CryptographicOperations.ZeroMemory(encryptionKey);
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

        Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeCreateResult =
            _nativeSessions.CreateOrReplaceIdentity(connectId, identityResult.Unwrap());
        if (nativeCreateResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native identity: {nativeCreateResult.UnwrapErr().Message}");
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

    public void ClearConnection(uint connectId) => _nativeSessions.Remove(connectId);

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

        SessionRecoveryRequest request = new();
        Result<SessionRecoveryResponse, NetworkFailure> restoreResponse =
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

    private async Task<Result<SessionRecoveryResponse, NetworkFailure>> ExecuteRestoreChannelByRetryModeAsync(
        SessionRecoveryRequest request,
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
            _ => Result<SessionRecoveryResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Unknown retry mode: {retryMode}"))
        };
    }

    private async Task<Result<SessionRecoveryResponse, NetworkFailure>> ExecuteWithAutoRetryAsync(
        SessionRecoveryRequest request,
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

    private async Task<Result<SessionRecoveryResponse, NetworkFailure>> ExecuteWithManualRetryAsync(
        SessionRecoveryRequest request,
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

    private async Task<Result<SessionRecoveryResponse, NetworkFailure>> ExecuteDirectRestoreAsync(
        SessionRecoveryRequest request,
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
            return Result<SessionRecoveryResponse, NetworkFailure>.Err(
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
        if (FailureClassification.IsProtocolStateMismatch(failure) ||
            FailureClassification.IsSessionExpired(failure))
        {
            Log.Warning(
                "[NETWORK-PROVIDER] Restore failure requires state cleanup. Type: {FailureType} ConnectId: {ConnectId}",
                failure.FailureType,
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
        SessionRecoveryResponse response,
        EcliptixSessionState sessionState,
        bool enablePendingRegistration)
    {
        return response.Result switch
        {
            SessionRecoveryResponse.Types.Result.SessionRecoveryResultRestored =>
                await HandleSessionRestoredAsync(response, sessionState, enablePendingRegistration)
                    .ConfigureAwait(false),
            SessionRecoveryResponse.Types.Result.SessionRecoveryResultNotFound =>
                await HandleSessionNotFoundAsync(sessionState.ConnectId),
            _ => Result<bool, NetworkFailure>.Ok(false)
        };
    }

    private async Task<Result<bool, NetworkFailure>> HandleSessionRestoredAsync(
        SessionRecoveryResponse response,
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

    public async Task<Result<byte[], NetworkFailure>> GetServerPublicKeyAsync(uint connectId)
    {
        Result<byte[], EcliptixProtocolFailure> cachedResult = _nativeSessions.GetServerPublicKey(connectId);
        if (cachedResult.IsOk)
        {
            return Result<byte[], NetworkFailure>.Ok(cachedResult.Unwrap());
        }

        Result<byte[], NetworkFailure> fetchResult =
            await FetchServerPreKeyBundleAsync(connectId, PubKeyExchangeType.InitialHandshake).ConfigureAwait(false);

        if (fetchResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(fetchResult.UnwrapErr());
        }

        Result<byte[], EcliptixProtocolFailure> keyResult = _nativeSessions.GetServerPublicKey(connectId);
        if (keyResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Server X25519 public key not available after fetch"));
        }

        return Result<byte[], NetworkFailure>.Ok(keyResult.Unwrap());
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

        Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeCreateResult =
            _nativeSessions.CreateOrReplaceIdentity(connectId, identityResult.Unwrap());
        if (nativeCreateResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create native identity: {nativeCreateResult.UnwrapErr().Message}");
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
        SessionRecoveryResponse peerSecrecyChannelState)
    {
        Result<Unit, EcliptixProtocolFailure> restoreResult =
            await RestoreNativeSessionFromStateAsync(currentState).ConfigureAwait(false);
        if (restoreResult.IsErr)
        {
            return restoreResult;
        }

        uint localSending = currentState.SendingChainIndex;
        uint localReceiving = currentState.ReceivingChainIndex;

        uint serverReceiving = (uint)peerSecrecyChannelState.ReceivingChainIndex;
        uint serverSending = (uint)peerSecrecyChannelState.SendingChainIndex;

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

    private static bool TryResolveMembershipGuid(ByteString membershipId, out Guid membershipGuid)
    {
        if (membershipId.IsEmpty)
        {
            membershipGuid = Guid.Empty;
            return false;
        }

        try
        {
            membershipGuid = Helpers.FromByteStringToGuid(membershipId);
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
                _applicationInstanceSettings.Value!.Membership?.MembershipId != null)
            {
                membershipGuid =
                    Helpers.FromByteStringToGuid(_applicationInstanceSettings.Value!.Membership!.MembershipId);
            }
            else
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput("Invalid membership identifier for restoration"));
            }
        }

        if (resolvedFromState &&
            _applicationInstanceSettings.IsSome &&
            _applicationInstanceSettings.Value!.Membership?.MembershipId != null)
        {
            Guid expectedMembershipId =
                Helpers.FromByteStringToGuid(_applicationInstanceSettings.Value!.Membership!.MembershipId);
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
        byte[]? decryptionKey = null;
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
            byte[] sealedStateBytes = state.NativeState.ToByteArray();
            Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeIdentityResult =
                NativeProtocolSystem.CreateIdentityFromSeed(masterKeyBytes, accountGuid.ToString());
            if (nativeIdentityResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(nativeIdentityResult.UnwrapErr());
            }

            EcliptixIdentityKeysWrapper nativeIdentity = nativeIdentityResult.Unwrap();
            Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> identityStoreResult =
                _nativeSessions.CreateOrReplaceIdentity(state.ConnectId, nativeIdentity);
            if (identityStoreResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(identityStoreResult.UnwrapErr());
            }

            decryptionKey = await _security.PlatformSecurityProvider.GetOrCreateSessionStateKeyAsync()
                .ConfigureAwait(false);

            Result<NativeProtocolSession, EcliptixProtocolFailure> nativeImportResult =
                _nativeSessions.CreateOrReplaceFromState(
                    state.ConnectId,
                    sealedStateBytes,
                    decryptionKey);
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
            if (decryptionKey != null)
            {
                CryptographicOperations.ZeroMemory(decryptionKey);
            }
        }
    }

    private static uint GenerateLogicalOperationId(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
    {
        Span<byte> hashBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];

        switch (serviceType)
        {
            case RpcServiceType.SignInInitRequest:
            case RpcServiceType.SignInCompleteRequest:
                {
                    Span<byte> semanticBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                    int written = Encoding.UTF8.GetBytes($"auth:signin:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
            case RpcServiceType.RegistrationInit:
            case RpcServiceType.RegistrationComplete:
                {
                    Span<byte> semanticBuffer = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                    int written = Encoding.UTF8.GetBytes($"auth:signup:{connectId}", semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
            case RpcServiceType.InitiateVerification:
                {
                    Span<byte> payloadHash = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                    SHA256.HashData(plainBuffer, payloadHash);

                    string semantic =
                        $"stream:{serviceType}:{connectId}:{DateTime.UtcNow.Ticks}:{Convert.ToHexString(payloadHash)}";
                    Span<byte> semanticBuffer = stackalloc byte[Encoding.UTF8.GetByteCount(semantic)];
                    int written = Encoding.UTF8.GetBytes(semantic, semanticBuffer);
                    SHA256.HashData(semanticBuffer[..written], hashBuffer);
                    break;
                }
            default:
                {
                    Span<byte> payloadHash = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
                    SHA256.HashData(plainBuffer, payloadHash);

                    string semantic = $"data:{serviceType}:{connectId}:{Convert.ToHexString(payloadHash)}";
                    Span<byte> semanticBuffer = stackalloc byte[Encoding.UTF8.GetByteCount(semantic)];
                    int written = Encoding.UTF8.GetBytes(semantic, semanticBuffer);
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
        uint envelopeId,
        EnvelopeType envelopeType,
        byte[] plainBuffer,
        string? correlationId)
    {
        Log.Information("[CLIENT-ENCRYPT] EncryptPayload called. ConnectId={ConnectId}, PlainBufferSize={Size}",
            connectId, plainBuffer.Length);

        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult =
            _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsErr)
        {
            Log.Error("[CLIENT-ENCRYPT] Failed to get native session for ConnectId={ConnectId}: {Error}",
                connectId, nativeSessionResult.UnwrapErr().Message);
            return Result<SecureEnvelope, NetworkFailure>.Err(nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();

        Result<byte[], EcliptixProtocolFailure> nativeCipher =
            nativeSession.Encrypt(plainBuffer, envelopeType, envelopeId, correlationId);
        if (nativeCipher.IsErr)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                nativeCipher.UnwrapErr().ToNetworkFailure());
        }

        try
        {
            SecureEnvelope envelope = SecureEnvelope.Parser.ParseFrom(nativeCipher.Unwrap());
            Log.Information(
                "[CLIENT-ENCRYPT] Encrypted envelope ready. ConnectId={ConnectId}, Meta={Meta}, Payload={Payload}, HeaderNonce={HeaderNonce}, DhPublicKey={DhPublicKey}, KyberCiphertext={KyberCiphertext}, RatchetEpoch={RatchetEpoch}",
                connectId,
                envelope.EncryptedMetadata.Length,
                envelope.EncryptedPayload.Length,
                envelope.HeaderNonce.Length,
                envelope.DhPublicKey.Length,
                envelope.KyberCiphertext.Length,
                envelope.RatchetEpoch.ToString());
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
        Result<SecureEnvelope, NetworkFailure> encryptResult = EncryptPayload(
            connectId,
            logicalOperationId,
            EnvelopeType.Request,
            plainBuffer,
            requestContext.CorrelationId);

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

        byte[] serializedEnvelope = envelope.ToByteArray();
        Result<Unit, EcliptixProtocolFailure> validateResult =
            NativeProtocolSession.ValidateEnvelope(serializedEnvelope);
        if (validateResult.IsErr)
        {
            return Result<byte[], NetworkFailure>.Err(validateResult.UnwrapErr().ToNetworkFailure());
        }

        Result<ProtocolDecryptResult, EcliptixProtocolFailure> decryptResult =
            nativeSession.Decrypt(serializedEnvelope);
        return decryptResult.IsErr
            ? Result<byte[], NetworkFailure>.Err(decryptResult.UnwrapErr().ToNetworkFailure())
            : Result<byte[], NetworkFailure>.Ok(decryptResult.Unwrap().Plaintext);
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

        _pendingPersistTasks.AddOrUpdate(connectId, persistTask, (_, _) => persistTask);
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

        byte[]? encryptionKey = null;
        try
        {
            encryptionKey = await _security.PlatformSecurityProvider.GetOrCreateSessionStateKeyAsync()
                .ConfigureAwait(false);
            Result<byte[], EcliptixProtocolFailure> exportResult = nativeResult.Unwrap().ExportSealedState(encryptionKey);
            if (exportResult.IsErr)
            {
                return;
            }

            byte[]? accountId = GetAccountIdBytes();
            if (accountId == null)
            {
                return;
            }

            EcliptixSessionState? existingState = await TryLoadStoredStateAsync(connectId, accountId)
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

            if (_applicationInstanceSettings.IsSome && state.MembershipId.IsEmpty)
            {
                state.MembershipId = _applicationInstanceSettings.Value!.Membership?.MembershipId ?? ByteString.Empty;
            }

            if (_applicationInstanceSettings.IsSome &&
                _applicationInstanceSettings.Value!.CurrentAccountId != null &&
                _applicationInstanceSettings.Value!.CurrentAccountId.Length > 0 &&
                state.AccountId.IsEmpty)
            {
                state.AccountId = _applicationInstanceSettings.Value!.CurrentAccountId;
            }

            await PersistSessionStateAsync(state, connectId, accountId).ConfigureAwait(false);
        }
        finally
        {
            if (encryptionKey != null)
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
            }
        }
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
                // ignored
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
        }
    }

    private void CancelOutageCompletion()
    {
        lock (_outageLock)
        {
            _outageCompletionSource.TrySetException(new OperationCanceledException("Provider shutting down"));
        }
    }

    private void DisposeConnections() => _nativeSessions.Dispose();

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
        Result<(uint connectId, byte[] accountId), NetworkFailure> prerequisitesResult =
            ValidateRecoveryPrerequisites();
        if (prerequisitesResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(prerequisitesResult.UnwrapErr());
        }

        (uint connectId, byte[] accountId) = prerequisitesResult.Unwrap();
        _nativeSessions.Remove(connectId);

        Result<EcliptixSessionState, NetworkFailure> stateResult =
            await LoadAndParseStoredState(connectId, accountId, failOnMissingState, failureMessage)
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

    private Result<(uint connectId, byte[] accountId), NetworkFailure> ValidateRecoveryPrerequisites()
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return Result<(uint, byte[]), NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        byte[]? accountId = GetAccountIdBytes();
        if (accountId == null)
        {
            return Result<(uint, byte[]), NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("AccountId not available for state restoration"));
        }

        return Result<(uint, byte[]), NetworkFailure>.Ok((connectId, accountId));
    }

    private async Task<Result<EcliptixSessionState, NetworkFailure>> LoadAndParseStoredState(
        uint connectId,
        byte[] accountId,
        bool failOnMissingState,
        string failureMessage)
    {
        Result<byte[], SecureStorageFailure> stateResult =
            await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), accountId)
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

    private async Task<EcliptixSessionState?> TryLoadStoredStateAsync(uint connectId, byte[] accountId)
    {
        Result<byte[], SecureStorageFailure> stateResult =
            await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), accountId)
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

    private byte[]? GetAccountIdBytes()
    {
        if (!_applicationInstanceSettings.IsSome)
        {
            return null;
        }

        ApplicationInstanceSettings settings = _applicationInstanceSettings.Value!;
        ByteString? accountId = settings.CurrentAccountId;
        if (accountId == null || accountId.IsEmpty)
        {
            return null;
        }

        return accountId.ToByteArray();
    }

    private async Task PersistSessionStateAsync(EcliptixSessionState state, uint connectId,
        byte[]? accountIdOverride = null)
    {
        byte[]? accountId = accountIdOverride ?? GetAccountIdBytes();
        if (accountId == null)
        {
            return;
        }

        Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                state.ToByteString(),
                span => _dependencies.SecureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString(),
                    accountId))
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

    public bool IsConnectionHealthy(uint connectId) => _nativeSessions.Get(connectId).IsOk;

    public async Task<Result<bool, NetworkFailure>> TryRestoreConnectionAsync(uint connectId)
    {
        return await WithChannelGate(connectId, async () =>
        {
            try
            {
                byte[]? accountId = GetAccountIdBytes();
                if (accountId == null)
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
                    await _dependencies.SecureProtocolStateStorage.LoadStateAsync(connectId.ToString(), accountId)
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

        if (networkResult.IsErr &&
            networkResult.UnwrapErr().FailureType == NetworkFailureType.MASTER_KEY_SHARES_NOT_FOUND)
        {
            Log.Warning(
                "[RecreateProtocolWithMasterKey] Server missing master key shares, falling back to fresh handshake");

            _nativeSessions.ClearServerPreKeyBundle(connectId);
            Log.Debug("[HANDSHAKE-RETRY] Cleared stale prekey bundle for connectId {ConnectId}", connectId);

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
        byte[]? rootKey = null;
        byte[]? proofInput = null;
        byte[]? proof = null;
        byte[]? clientNonce = null;
        byte[]? serverNonce = null;

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

            EcliptixIdentityKeysWrapper nativeIdentity = nativeIdentityResult.Unwrap();

            _nativeSessions.Remove(connectId);
            CancelOperationsForConnection(connectId);
            const PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect;
            Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> nativeIdentityStoreResult =
                _nativeSessions.CreateOrReplaceIdentity(connectId, nativeIdentity);
            if (nativeIdentityStoreResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(
                    nativeIdentityStoreResult.UnwrapErr().ToNetworkFailure());
            }

            Result<byte[], NetworkFailure> bundleResult =
                await FetchServerPreKeyBundleAsync(connectId, exchangeType).ConfigureAwait(false);
            if (bundleResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(bundleResult.UnwrapErr());
            }

            Log.Debug(
                "[AUTH-HANDSHAKE] Using fresh server prekey bundle for connectId {ConnectId}, exchangeType={ExchangeType}, length: {Length}",
                connectId, exchangeType, bundleResult.Unwrap().Length);

            Result<uint, NetworkFailure> chainLimitResult = ResolveChainLimit(exchangeType);
            if (chainLimitResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(chainLimitResult.UnwrapErr());
            }

            Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure> handshakeStart =
                NativeHandshakeInitiator.Start(nativeIdentity, bundleResult.Unwrap(), chainLimitResult.Unwrap());
            if (handshakeStart.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(handshakeStart.UnwrapErr().ToNetworkFailure());
            }

            NativeHandshakeInitiatorStart handshakeInfo = handshakeStart.Unwrap();
            _nativeSessions.StoreHandshakeInitiator(connectId, handshakeInfo.Initiator);
            byte[] handshakeInit = handshakeInfo.HandshakeInit;

            Result<byte[], EcliptixProtocolFailure> serverNonceResult = _nativeSessions.GetServerNonce(connectId);
            if (serverNonceResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(serverNonceResult.UnwrapErr().ToNetworkFailure());
            }

            serverNonce = serverNonceResult.Unwrap();
            if (serverNonce.Length != AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(
                        $"Server nonce has invalid length (expected {AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH})"));
            }

            clientNonce = RandomNumberGenerator.GetBytes(AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH);
            RpcRequestContext requestContext = RpcRequestContext.CreateNew();

            string appDeviceId = Convert.ToBase64String(_dependencies.RpcMetaDataProvider.DeviceId.ToByteArray());
            string appInstanceId = Convert.ToBase64String(_dependencies.RpcMetaDataProvider.AppInstanceId.ToByteArray());

            rootKey = DeriveRootKeyFromMasterKey(masterKeyBytes, accountGuid);
            proofInput = BuildAuthenticatedEstablishProofInput(
                membershipIdentifier.ToByteArray(),
                accountIdentifier.ToByteArray(),
                masterKeyFingerprint,
                handshakeInit,
                clientNonce,
                serverNonce,
                requestContext.IdempotencyKey,
                appDeviceId,
                appInstanceId);
            proof = HMACSHA256.HashData(rootKey, proofInput);

            AuthenticatedSessionHandshakeRequest authenticatedRequest = new()
            {
                Identity =
                    new AuthenticatedSessionHandshakeRequest.Types.Identity
                    {
                        MembershipId = membershipIdentifier,
                        AccountId = accountIdentifier
                    },
                Cryptography = new AuthenticatedSessionHandshakeRequest.Types.Cryptography
                {
                    MasterKeyFingerprint = ByteString.CopyFrom(masterKeyFingerprint),
                    HandshakeInit = ByteString.CopyFrom(handshakeInit),
                    ClientNonce = ByteString.CopyFrom(clientNonce),
                    Proof = ByteString.CopyFrom(proof),
                    ServerNonce = ByteString.CopyFrom(serverNonce)
                }
            };

            _nativeSessions.ClearServerNonce(connectId);

            Result<SecureEnvelope, NetworkFailure> serverResponseResult =
                await _dependencies.RpcServiceManager.EstablishAuthenticatedSecrecyChannelAsync(
                    _services.ConnectivityService,
                    authenticatedRequest,
                    requestContext).ConfigureAwait(false);

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

            Result<Unit, NetworkFailure> processResult =
                ProcessAuthenticatedHandshakeResponse(
                    serverResponseResult.Unwrap(),
                    certificatePinningService.Value!,
                    connectId);

            if (processResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(processResult.UnwrapErr());
            }

            Result<EcliptixSessionState, NetworkFailure> stateResult = await CreateSessionStateAsync(
                connectId,
                handshakeInit,
                exchangeType,
                membershipIdentifier,
                accountIdentifier,
                null).ConfigureAwait(false);
            if (stateResult.IsErr)
            {
                await CleanupFailedAuthenticationAsync(connectId).ConfigureAwait(false);
                return Result<Unit, NetworkFailure>.Err(stateResult.UnwrapErr());
            }

            await PersistSessionStateAsync(stateResult.Unwrap(), connectId, accountIdentifier.ToByteArray())
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
            if (proofInput != null)
            {
                CryptographicOperations.ZeroMemory(proofInput);
            }

            if (proof != null)
            {
                CryptographicOperations.ZeroMemory(proof);
            }

            if (clientNonce != null)
            {
                CryptographicOperations.ZeroMemory(clientNonce);
            }

            if (serverNonce != null)
            {
                CryptographicOperations.ZeroMemory(serverNonce);
            }

            if (rootKey != null)
            {
                CryptographicOperations.ZeroMemory(rootKey);
            }

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
        _nativeSessions.ClearHandshakeInitiator(connectId);
        _nativeSessions.ClearServerPreKeyBundle(connectId);
        _nativeSessions.ClearServerNonce(connectId);
        _nativeSessions.ClearServerPublicKey(connectId);

        Result<Unit, SecureStorageFailure> deleteResult =
            await _dependencies.SecureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning(
                "[CLIENT-AUTH-CLEANUP] Failed to delete protocol state during authentication cleanup. ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }
    }

    private static byte[] BuildAuthenticatedEstablishProofInput(
        byte[] membershipId,
        byte[] accountId,
        byte[] masterKeyFingerprint,
        byte[] handshakeInit,
        byte[] clientNonce,
        byte[] serverNonce,
        string idempotencyKey,
        string appDeviceId,
        string appInstanceId)
    {
        byte[] idempotencyBytes = Encoding.UTF8.GetBytes(idempotencyKey);
        byte[] appDeviceBytes = Encoding.UTF8.GetBytes(appDeviceId);
        byte[] appInstanceBytes = Encoding.UTF8.GetBytes(appInstanceId);
        byte[][] parts =
        [
            AuthenticatedEstablishProofContext,
            membershipId,
            accountId,
            masterKeyFingerprint,
            handshakeInit,
            clientNonce,
            serverNonce,
            idempotencyBytes,
            appDeviceBytes,
            appInstanceBytes
        ];

        int totalLength = 0;
        foreach (byte[] part in parts)
        {
            totalLength += sizeof(uint) + part.Length;
        }

        byte[] buffer = new byte[totalLength];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, sizeof(uint)), (uint)part.Length);
            offset += sizeof(uint);
            part.CopyTo(buffer, offset);
            offset += part.Length;
        }

        return buffer;
    }
}
