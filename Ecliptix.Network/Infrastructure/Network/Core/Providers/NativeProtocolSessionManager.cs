using System.Collections.Concurrent;
using Ecliptix.Protocol.System.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

/// <summary>
/// Manages native protocol sessions keyed by connectId. Keeps creation, lookup, and disposal in one place
/// to make the NetworkProvider leaner while we migrate off the managed ratchet.
/// </summary>
internal sealed class NativeProtocolSessionManager : IDisposable
{
    private readonly ConcurrentDictionary<uint, NativeProtocolSession> _sessions = new();
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

        // Dispose any existing session for this connectId
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

    public Result<NativeProtocolSession, EcliptixProtocolFailure> CreateOrReplaceFromRoot(
        uint connectId,
        EcliptixIdentityKeysWrapper identity,
        byte[] rootKey,
        byte[] peerBundle,
        bool isInitiator,
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
            NativeProtocolSession.CreateFromRoot(identity, rootKey, peerBundle, isInitiator);
        if (createResult.IsErr)
        {
            return createResult;
        }

        NativeProtocolSession session = createResult.Unwrap();
        session.SetEventHandler(onProtocolStateChanged);
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
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeProtocolSessionManager()
    {
        Dispose();
    }
}
