using System.Collections.Concurrent;
using System.Security.Cryptography;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

internal sealed class NativeProtocolSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<uint, NativeProtocolSession> _sessions = new();
    private readonly ConcurrentDictionary<uint, EcliptixIdentityKeysWrapper> _identities = new();
    private readonly ConcurrentDictionary<uint, NativeHandshakeInitiator> _pendingInitiators = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverPreKeyBundles = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverNonces = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverPublicKeys = new();
    private bool _disposed;

    public Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateOrReplaceIdentity(
        uint connectId,
        EcliptixIdentityKeysWrapper identity,
        Action<uint>? _ = null)
    {
        if (_disposed)
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_sessions.TryRemove(connectId, out NativeProtocolSession? existing))
        {
            existing.Dispose();
        }
        if (_pendingInitiators.TryRemove(connectId, out NativeHandshakeInitiator? pending))
        {
            pending.Dispose();
        }
        if (_identities.TryRemove(connectId, out EcliptixIdentityKeysWrapper? existingIdentity))
        {
            existingIdentity.Dispose();
        }
        ClearCachedKeys(connectId);

        _identities[connectId] = identity;
        return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Ok(identity);
    }

    public Result<NativeProtocolSession, EcliptixProtocolFailure> Get(uint connectId)
    {
        if (_disposed)
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_sessions.TryGetValue(connectId, out NativeProtocolSession? session))
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(session);
        }

        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("Connection unavailable - session not found"));
    }

    public Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> GetIdentity(uint connectId)
    {
        if (_disposed)
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_identities.TryGetValue(connectId, out EcliptixIdentityKeysWrapper? identity))
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Ok(identity);
        }

        return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("Identity not found for connection"));
    }

    public bool Has(uint connectId) => !_disposed && _sessions.ContainsKey(connectId);

    public void StoreServerPreKeyBundle(uint connectId, byte[] bundle)
    {
        if (_disposed)
        {
            return;
        }

        _serverPreKeyBundles[connectId] = bundle;
    }

    public void StoreServerNonce(uint connectId, byte[] serverNonce)
    {
        if (_disposed)
        {
            return;
        }

        _serverNonces[connectId] = serverNonce;
    }

    public Result<byte[], EcliptixProtocolFailure> GetServerPreKeyBundle(uint connectId)
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_serverPreKeyBundles.TryGetValue(connectId, out byte[]? key))
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok(key);
        }

        return Result<byte[], EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("No per-connection prekey bundle found"));
    }

    public Result<byte[], EcliptixProtocolFailure> GetServerNonce(uint connectId)
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_serverNonces.TryGetValue(connectId, out byte[]? nonce))
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok(nonce);
        }

        return Result<byte[], EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("No server nonce found"));
    }

    public void ClearServerPreKeyBundle(uint connectId)
    {
        if (_serverPreKeyBundles.TryRemove(connectId, out byte[]? key))
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public void ClearServerNonce(uint connectId)
    {
        if (_serverNonces.TryRemove(connectId, out byte[]? nonce))
        {
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    public void StoreServerPublicKey(uint connectId, byte[] serverPublicKey)
    {
        if (_disposed)
        {
            return;
        }

        _serverPublicKeys[connectId] = serverPublicKey;
    }

    public Result<byte[], EcliptixProtocolFailure> GetServerPublicKey(uint connectId)
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_serverPublicKeys.TryGetValue(connectId, out byte[]? key))
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok(key);
        }

        return Result<byte[], EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("No per-connection server public key found"));
    }

    public void ClearServerPublicKey(uint connectId)
    {
        if (_serverPublicKeys.TryRemove(connectId, out byte[]? key))
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public IEnumerable<uint> ActiveConnectionIds()
    {
        if (_disposed)
        {
            yield break;
        }

        foreach ((uint id, _) in _sessions)
        {
            yield return id;
        }
    }

    public void StoreHandshakeInitiator(uint connectId, NativeHandshakeInitiator initiator)
    {
        if (_disposed)
        {
            return;
        }

        if (_pendingInitiators.TryRemove(connectId, out NativeHandshakeInitiator? existing))
        {
            existing.Dispose();
        }

        _pendingInitiators[connectId] = initiator;
    }

    public Result<NativeHandshakeInitiator, EcliptixProtocolFailure> GetHandshakeInitiator(uint connectId)
    {
        if (_disposed)
        {
            return Result<NativeHandshakeInitiator, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_pendingInitiators.TryGetValue(connectId, out NativeHandshakeInitiator? initiator))
        {
            return Result<NativeHandshakeInitiator, EcliptixProtocolFailure>.Ok(initiator);
        }

        return Result<NativeHandshakeInitiator, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("Handshake initiator not found for connection"));
    }

    public void ClearHandshakeInitiator(uint connectId)
    {
        if (_pendingInitiators.TryRemove(connectId, out NativeHandshakeInitiator? initiator))
        {
            initiator.Dispose();
        }
    }

    public void StoreSession(uint connectId, NativeProtocolSession session)
    {
        if (_disposed)
        {
            session.Dispose();
            return;
        }

        if (_sessions.TryRemove(connectId, out NativeProtocolSession? existing))
        {
            existing.Dispose();
        }

        _sessions[connectId] = session;
    }

    public Result<NativeProtocolSession, EcliptixProtocolFailure> CreateOrReplaceFromState(
        uint connectId,
        byte[] stateBytes)
    {
        if (_disposed)
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_sessions.TryRemove(connectId, out NativeProtocolSession? existing))
        {
            existing.Dispose();
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> importResult =
            NativeProtocolSession.Import(stateBytes);
        if (importResult.IsErr)
        {
            return importResult;
        }

        NativeProtocolSession session = importResult.Unwrap();
        _sessions[connectId] = session;
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(session);
    }

    public void Remove(uint connectId)
    {
        if (_sessions.TryRemove(connectId, out NativeProtocolSession? session))
        {
            session.Dispose();
        }
        if (_pendingInitiators.TryRemove(connectId, out NativeHandshakeInitiator? initiator))
        {
            initiator.Dispose();
        }
        if (_identities.TryRemove(connectId, out EcliptixIdentityKeysWrapper? identity))
        {
            identity.Dispose();
        }
        ClearCachedKeys(connectId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach ((_, NativeProtocolSession session) in _sessions)
        {
            session.Dispose();
        }
        _sessions.Clear();
        foreach ((_, NativeHandshakeInitiator initiator) in _pendingInitiators)
        {
            initiator.Dispose();
        }
        _pendingInitiators.Clear();
        foreach ((_, EcliptixIdentityKeysWrapper identity) in _identities)
        {
            identity.Dispose();
        }
        _identities.Clear();
        ClearAllCachedKeys();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeProtocolSessionManager()
    {
        Dispose();
    }

    private void ClearCachedKeys(uint connectId)
    {
        ClearServerPreKeyBundle(connectId);
        ClearServerNonce(connectId);
        ClearServerPublicKey(connectId);
    }

    private void ClearAllCachedKeys()
    {
        foreach ((uint connectId, _) in _serverPreKeyBundles)
        {
            ClearServerPreKeyBundle(connectId);
        }

        foreach ((uint connectId, _) in _serverNonces)
        {
            ClearServerNonce(connectId);
        }

        foreach ((uint connectId, _) in _serverPublicKeys)
        {
            ClearServerPublicKey(connectId);
        }
    }
}
