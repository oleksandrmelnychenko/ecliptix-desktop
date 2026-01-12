using System.Collections.Concurrent;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

internal sealed class NativeProtocolSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<uint, NativeProtocolSession> _sessions = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverKyberKeys = new();
    private readonly ConcurrentDictionary<uint, byte[]> _serverNonces = new();
    private bool _disposed;

    public Result<NativeProtocolSession, EcliptixProtocolFailure> CreateOrReplace(
        uint connectId,
        EcliptixIdentityKeysWrapper identity,
        Action<uint>? onProtocolStateChanged = null)
    {
        if (_disposed)
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(NativeProtocolSessionManager)));
        }

        if (_sessions.TryRemove(connectId, out NativeProtocolSession? existing))
        {
            existing.Dispose();
        }

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
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(NativeProtocolSessionManager)));
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
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(NativeProtocolSessionManager)));
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
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(NativeProtocolSessionManager)));
        }

        if (_serverNonces.TryGetValue(connectId, out byte[]? nonce))
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok(nonce);
        }

        return Result<byte[], EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("No server nonce found"));
    }

    public void ClearServerKyberKey(uint connectId) => _serverKyberKeys.TryRemove(connectId, out _);

    public void ClearServerNonce(uint connectId) => _serverNonces.TryRemove(connectId, out _);

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
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(NativeProtocolSessionManager)));
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
        _serverKyberKeys.TryRemove(connectId, out _);
        _serverNonces.TryRemove(connectId, out _);
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
        _serverKyberKeys.Clear();
        _serverNonces.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeProtocolSessionManager()
    {
        Dispose();
    }
}
