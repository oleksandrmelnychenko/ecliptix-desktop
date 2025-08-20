using System.Security.Cryptography;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

public sealed class RatchetRecovery(uint maxSkippedMessages = 1000) : IDisposable
{
    private readonly Dictionary<uint, EcliptixMessageKey> _skippedMessageKeys = new();
    private readonly Lock _lock = new();
    private bool _disposed;

    public Result<Option<EcliptixMessageKey>, EcliptixProtocolFailure> TryRecoverMessageKey(uint messageIndex)
    {
        if (_disposed)
            return Result<Option<EcliptixMessageKey>, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(RatchetRecovery)));

        lock (_lock)
        {
            return Result<Option<EcliptixMessageKey>, EcliptixProtocolFailure>.Ok(
                _skippedMessageKeys.Remove(messageIndex, out EcliptixMessageKey? key)
                    ? Option<EcliptixMessageKey>.Some(key)
                    : Option<EcliptixMessageKey>.None);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> StoreSkippedMessageKeys(
        byte[] currentChainKey,
        uint fromIndex,
        uint toIndex)
    {
        if (_disposed)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(RatchetRecovery)));

        if (toIndex <= fromIndex)
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        lock (_lock)
        {
            uint skippedCount = toIndex - fromIndex;
            if (_skippedMessageKeys.Count + skippedCount > maxSkippedMessages)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Too many skipped messages: {_skippedMessageKeys.Count + skippedCount} > {maxSkippedMessages}"));
            }

            using ScopedSecureMemoryCollection secureMemory = new();
            ScopedSecureMemory chainKeyMemory = secureMemory.Allocate(Constants.X25519KeySize);
            currentChainKey.CopyTo(chainKeyMemory.AsSpan());

            for (uint i = fromIndex; i < toIndex; i++)
            {
                Result<EcliptixMessageKey, EcliptixProtocolFailure> msgKeyResult =
                    DeriveMessageKey(chainKeyMemory.AsSpan(), i);
                if (msgKeyResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(msgKeyResult.UnwrapErr());

                _skippedMessageKeys[i] = msgKeyResult.Unwrap();

                Result<Unit, EcliptixProtocolFailure> advanceResult = AdvanceChainKey(chainKeyMemory.AsSpan());
                if (advanceResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(advanceResult.UnwrapErr());
            }

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    private static Result<EcliptixMessageKey, EcliptixProtocolFailure> DeriveMessageKey(ReadOnlySpan<byte> chainKey,
        uint messageIndex)
    {
        using SecurePooledArray<byte> msgKey = SecureArrayPool.Rent<byte>(Constants.AesKeySize);

        global::System.Security.Cryptography.HKDF.DeriveKey(
            global::System.Security.Cryptography.HashAlgorithmName.SHA256,
            ikm: chainKey,
            output: msgKey.AsSpan(),
            salt: null,
            info: Constants.MsgInfo
        );

        return EcliptixMessageKey.New(messageIndex, msgKey.AsSpan());
    }

    private static Result<Unit, EcliptixProtocolFailure> AdvanceChainKey(Span<byte> chainKey)
    {
        using SecurePooledArray<byte> nextChainKey = SecureArrayPool.Rent<byte>(Constants.X25519KeySize);

        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: chainKey,
            output: nextChainKey.AsSpan(),
            salt: null,
            info: Constants.ChainInfo
        );

        nextChainKey.AsSpan().CopyTo(chainKey);
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public void CleanupOldKeys(uint beforeIndex)
    {
        if (_disposed) return;

        lock (_lock)
        {
            List<uint> keysToRemove = _skippedMessageKeys.Keys.Where(index => index < beforeIndex).ToList();
            foreach (uint key in keysToRemove)
            {
                if (_skippedMessageKeys.TryGetValue(key, out EcliptixMessageKey? msgKey))
                {
                    msgKey.Dispose();
                    _skippedMessageKeys.Remove(key);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            foreach (EcliptixMessageKey key in _skippedMessageKeys.Values)
            {
                key.Dispose();
            }

            _skippedMessageKeys.Clear();
        }

        _disposed = true;
    }
}