using System.Collections.Concurrent;
using System.Security.Cryptography;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

internal sealed class NativeProtocolSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<uint, NativeProtocolSession> _sessions = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverKyberKeys = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverNonces = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverPublicKeys = new();
    private bool _disposed;

    public Result<NativeProtocolSession, EcliptixProtocolFailure> CreateOrReplace(
        uint connectId,
        EcliptixIdentityKeysWrapper identity,
        Action<uint>? onProtocolStateChanged = null)
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
        ClearCachedKeys(connectId);
        ClearCachedKeys(connectId);

        Result<NativeProtocolSession, EcliptixProtocolFailure> createResult =
            NativeProtocolSystem.CreateSessionAdapter(identity, onProtocolStateChanged);
        if (createResult.IsErr)
        {
            return createResult;
        }

        NativeProtocolSession session = createResult.Unwrap();
        _sessions[connectId] = session;
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(session);
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

    public bool Has(uint connectId) => !_disposed && _sessions.ContainsKey(connectId);

    public void StoreServerKyberKey(uint connectId, byte[] kyberPublicKey)
    {
        if (_disposed)
        {
            return;
        }

        _serverKyberKeys[connectId] = kyberPublicKey;
    }

    public void StoreServerNonce(uint connectId, byte[] serverNonce)
    {
        if (_disposed)
        {
            return;
        }

        _serverNonces[connectId] = serverNonce;
    }

    public Result<byte[], EcliptixProtocolFailure> GetServerKyberKey(uint connectId)
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(NativeProtocolSessionManager)));
        }

        if (_serverKyberKeys.TryGetValue(connectId, out byte[]? key))
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok(key);
        }

        return Result<byte[], EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("No per-connection Kyber key found"));
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

    public void ClearServerKyberKey(uint connectId)
    {
        if (_serverKyberKeys.TryRemove(connectId, out byte[]? key))
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

    public Result<NativeProtocolSession, EcliptixProtocolFailure> CreateOrReplaceFromState(
        uint connectId,
        EcliptixIdentityKeysWrapper identity,
        byte[] stateBytes,
        Action<uint>? onProtocolStateChanged = null)
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
            NativeProtocolSession.Import(identity, stateBytes);
        if (importResult.IsErr)
        {
            return importResult;
        }

        NativeProtocolSession session = importResult.Unwrap();
        session.SetEventHandler(onProtocolStateChanged);
        _sessions[connectId] = session;
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(session);
    }

    public void Remove(uint connectId)
    {
        if (_sessions.TryRemove(connectId, out NativeProtocolSession? session))
        {
            session.Dispose();
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
        ClearServerKyberKey(connectId);
        ClearServerNonce(connectId);
        ClearServerPublicKey(connectId);
    }

    private void ClearAllCachedKeys()
    {
        foreach ((uint connectId, _) in _serverKyberKeys)
        {
            ClearServerKyberKey(connectId);
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
