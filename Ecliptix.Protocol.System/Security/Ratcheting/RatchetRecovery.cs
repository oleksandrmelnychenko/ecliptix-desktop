using System.Security.Cryptography;
using Ecliptix.Protocol.System.Interfaces;
using Ecliptix.Protocol.System.Models.Keys;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Security.Ratcheting;

internal sealed class RatchetRecovery(uint maxSkippedMessages = 1000) : IKeyProvider, IDisposable
{
    private readonly Dictionary<uint, SodiumSecureMemoryHandle> _skippedMessageKeys = new();
    private readonly Lock _lock = new();
    private bool _disposed;

    public Result<T, EcliptixProtocolFailure> ExecuteWithKey<T>(uint keyIndex, Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation)
    {
        if (_disposed)
        {
            return Result<T, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(RatchetRecovery)));
        }

        lock (_lock)
        {
            if (!_skippedMessageKeys.TryGetValue(keyIndex, out SodiumSecureMemoryHandle? keyHandle))
            {
                return Result<T, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput($"Skipped key with index {keyIndex} not found."));
            }

            Result<T, SodiumFailure> sodiumResult = keyHandle.WithReadAccess(keyMaterial =>
            {
                Result<T, EcliptixProtocolFailure> opResult = operation(keyMaterial);
                return opResult.IsOk
                    ? Result<T, SodiumFailure>.Ok(opResult.Unwrap())
                    : Result<T, SodiumFailure>.Err(SodiumFailure.InvalidOperation(opResult.UnwrapErr().Message));
            });

            return sodiumResult.IsOk
                ? Result<T, EcliptixProtocolFailure>.Ok(sodiumResult.Unwrap())
                : Result<T, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.Generic(sodiumResult.UnwrapErr().Message));
        }
    }

    public Result<Option<RatchetChainKey>, EcliptixProtocolFailure> TryRecoverMessageKey(uint messageIndex)
    {
        if (_disposed)
        {
            return Result<Option<RatchetChainKey>, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(RatchetRecovery)));
        }

        lock (_lock)
        {
            if (_skippedMessageKeys.TryGetValue(messageIndex, out SodiumSecureMemoryHandle? _))
            {
                RatchetChainKey messageKey = new(messageIndex, this);
                return Result<Option<RatchetChainKey>, EcliptixProtocolFailure>.Ok(
                    Option<RatchetChainKey>.Some(messageKey));
            }

            return Result<Option<RatchetChainKey>, EcliptixProtocolFailure>.Ok(
                Option<RatchetChainKey>.None);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> StoreSkippedMessageKeys(
        byte[] currentChainKey,
        uint fromIndex,
        uint toIndex)
    {
        if (_disposed)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(RatchetRecovery)));
        }

        if (toIndex <= fromIndex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

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
            ScopedSecureMemory chainKeyMemory = secureMemory.Allocate(Constants.X_25519_KEY_SIZE);
            currentChainKey.CopyTo(chainKeyMemory.AsSpan());

            for (uint i = fromIndex; i < toIndex; i++)
            {
                Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> msgKeyResult =
                    DeriveSecureMessageKey(chainKeyMemory.AsSpan(), i);
                if (msgKeyResult.IsErr)
                {
                    return Result<Unit, EcliptixProtocolFailure>.Err(msgKeyResult.UnwrapErr());
                }

                _skippedMessageKeys[i] = msgKeyResult.Unwrap();

                Result<Unit, EcliptixProtocolFailure> advanceResult = AdvanceChainKey(chainKeyMemory.AsSpan());
                if (advanceResult.IsErr)
                {
                    return Result<Unit, EcliptixProtocolFailure>.Err(advanceResult.UnwrapErr());
                }
            }

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> DeriveSecureMessageKey(ReadOnlySpan<byte> chainKey,
        uint messageIndex)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> secureHandleResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X_25519_KEY_SIZE);

        if (secureHandleResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic($"Failed to allocate secure memory for key {messageIndex}."));
        }

        SodiumSecureMemoryHandle secureHandle = secureHandleResult.Unwrap();

        using SecurePooledArray<byte> msgKey = SecureArrayPool.Rent<byte>(Constants.X_25519_KEY_SIZE);

        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: chainKey,
            output: msgKey.AsSpan(),
            salt: null,
            info: Constants.MsgInfo
        );

        Result<Unit, SodiumFailure> writeResult = secureHandle.Write(msgKey.AsSpan());
        if (writeResult.IsErr)
        {
            secureHandle.Dispose();
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                writeResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(secureHandle);
    }

    private static Result<Unit, EcliptixProtocolFailure> AdvanceChainKey(Span<byte> chainKey)
    {
        using SecurePooledArray<byte> nextChainKey = SecureArrayPool.Rent<byte>(Constants.X_25519_KEY_SIZE);

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
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            List<uint> keysToRemove = _skippedMessageKeys.Keys.Where(index => index < beforeIndex).ToList();
            foreach (uint key in keysToRemove)
            {
                if (_skippedMessageKeys.Remove(key, out SodiumSecureMemoryHandle? removedHandle))
                {
                    removedHandle.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            foreach (SodiumSecureMemoryHandle handle in _skippedMessageKeys.Values)
            {
                handle.Dispose();
            }
            _skippedMessageKeys.Clear();
        }

        _disposed = true;
    }
}
