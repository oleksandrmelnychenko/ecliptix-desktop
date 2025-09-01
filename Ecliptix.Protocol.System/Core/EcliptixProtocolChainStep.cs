using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Protocol.System.Core;

public sealed class EcliptixProtocolChainStep : IDisposable
{
    private const uint DefaultCacheWindowSize = 1000;

    private static readonly Result<Unit, EcliptixProtocolFailure> OkResult =
        Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

    private readonly uint _cacheWindow;

    private readonly ChainStepType _stepType;

    private SodiumSecureMemoryHandle _chainKeyHandle;

    private readonly SortedDictionary<uint, EcliptixMessageKey> _messageKeys;

    private uint _currentIndex;

    private SodiumSecureMemoryHandle? _dhPrivateKeyHandle;

    private byte[]? _dhPublicKey;

    private bool _disposed;

    private EcliptixProtocolChainStep(
        ChainStepType stepType,
        SodiumSecureMemoryHandle chainKeyHandle,
        SodiumSecureMemoryHandle? dhPrivateKeyHandle,
        byte[]? dhPublicKey,
        uint cacheWindowSize)
    {
        _stepType = stepType;
        _chainKeyHandle = chainKeyHandle;
        _dhPrivateKeyHandle = dhPrivateKeyHandle;
        _dhPublicKey = dhPublicKey;
        _cacheWindow = cacheWindowSize;
        _currentIndex = 0;
        _disposed = false;
        _messageKeys = new SortedDictionary<uint, EcliptixMessageKey>();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _chainKeyHandle.Dispose();
        _dhPrivateKeyHandle?.Dispose();
        if (_dhPublicKey != null) SodiumInterop.SecureWipe(_dhPublicKey);

        _chainKeyHandle = null!;
        _dhPrivateKeyHandle = null;
        _dhPublicKey = null;
    }

    public Result<uint, EcliptixProtocolFailure> GetCurrentIndex()
    {
        return _disposed
            ? Result<uint, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)))
            : Result<uint, EcliptixProtocolFailure>.Ok(_currentIndex);
    }

    internal Result<byte[], EcliptixProtocolFailure> GetCurrentChainKey()
    {
        if (_disposed)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        return _chainKeyHandle.ReadBytes(Constants.X25519KeySize)
            .MapSodiumFailure();
    }

    internal Result<Unit, EcliptixProtocolFailure> SetCurrentIndex(uint value)
    {
        if (_disposed)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        if (_currentIndex != value)
        {
            _currentIndex = value;
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public static Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> Create(
        ChainStepType stepType,
        byte[] initialChainKey,
        byte[]? initialDhPrivateKey,
        byte[]? initialDhPublicKey,
        uint cacheWindowSize = DefaultCacheWindowSize)
    {
        Result<Unit, EcliptixProtocolFailure> keyValidation = ValidateInitialChainKey(initialChainKey);
        if (keyValidation.IsErr)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(keyValidation.UnwrapErr());

        Result<(SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned), EcliptixProtocolFailure> dhKeysResult =
            ValidateAndPrepareDhKeys(initialDhPrivateKey, initialDhPublicKey);
        if (dhKeysResult.IsErr)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(dhKeysResult.UnwrapErr());

        (SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned) dhInfo = dhKeysResult.Unwrap();

        Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> chainKeyResult = AllocateAndWriteChainKey(initialChainKey);
        if (chainKeyResult.IsErr)
        {
            dhInfo.dhPrivateKeyHandle?.Dispose();
            if (dhInfo.dhPublicKeyCloned != null) SodiumInterop.SecureWipe(dhInfo.dhPublicKeyCloned);
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(chainKeyResult.UnwrapErr());
        }

        SodiumSecureMemoryHandle chainKeyHandle = chainKeyResult.Unwrap();
        uint actualCacheWindow = cacheWindowSize > 0 ? cacheWindowSize : DefaultCacheWindowSize;
        EcliptixProtocolChainStep step = new(
            stepType,
            chainKeyHandle,
            dhInfo.dhPrivateKeyHandle,
            dhInfo.dhPublicKeyCloned,
            actualCacheWindow);

        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(step);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateInitialChainKey(byte[] initialChainKey)
    {
        return initialChainKey.Length == Constants.X25519KeySize
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput($"Initial chain key must be {Constants.X25519KeySize} bytes."));
    }

    private static Result<(SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned),
            EcliptixProtocolFailure>
        ValidateAndPrepareDhKeys(byte[]? initialDhPrivateKey, byte[]? initialDhPublicKey)
    {
        if (initialDhPrivateKey == null && initialDhPublicKey == null)
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Ok((null, null));

        if (initialDhPrivateKey == null || initialDhPublicKey == null)
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Both DH private and public keys must be provided, or neither."));

        if (initialDhPrivateKey.Length != Constants.X25519PrivateKeySize)
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Initial DH private key must be {Constants.X25519PrivateKeySize} bytes."));

        if (initialDhPublicKey.Length != Constants.X25519KeySize)
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Initial DH public key must be {Constants.X25519KeySize} bytes."));

        SodiumSecureMemoryHandle? dhPrivateKeyHandle = null;
        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize);
            if (allocResult.IsErr)
                return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                    allocResult.UnwrapErr().ToEcliptixProtocolFailure());

            dhPrivateKeyHandle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = dhPrivateKeyHandle.Write(initialDhPrivateKey);
            if (writeResult.IsErr)
            {
                dhPrivateKeyHandle.Dispose();
                return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                    writeResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            byte[] dhPublicKeyCloned = (byte[])initialDhPublicKey.Clone();
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Ok(
                (dhPrivateKeyHandle, dhPublicKeyCloned));
        }
        catch (Exception ex)
        {
            dhPrivateKeyHandle?.Dispose();
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Unexpected error preparing DH keys.", ex));
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> AllocateAndWriteChainKey(
        byte[] initialChainKey)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize);
        if (allocResult.IsErr)
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                allocResult.UnwrapErr().ToEcliptixProtocolFailure());

        SodiumSecureMemoryHandle chainKeyHandle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = chainKeyHandle.Write(initialChainKey);
        if (!writeResult.IsErr) return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(chainKeyHandle);
        chainKeyHandle.Dispose();
        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
            writeResult.UnwrapErr().ToEcliptixProtocolFailure());
    }

    internal Result<EcliptixMessageKey, EcliptixProtocolFailure> GetOrDeriveKeyFor(uint targetIndex)
    {
        if (_disposed)
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        if (_messageKeys.TryGetValue(targetIndex, out EcliptixMessageKey? cachedKey))
        {
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Ok(cachedKey);
        }

        Result<uint, EcliptixProtocolFailure> currentIndexResult = GetCurrentIndex();
        if (currentIndexResult.IsErr)
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

        uint currentIndex = currentIndexResult.Unwrap();

        if (targetIndex <= currentIndex)
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"[{_stepType}] Requested index {targetIndex} is not future (current: {currentIndex}) and not cached."));

        Result<byte[], EcliptixProtocolFailure> chainKeyResult = _chainKeyHandle.ReadBytes(Constants.X25519KeySize)
            .MapSodiumFailure();
        if (chainKeyResult.IsErr)
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(chainKeyResult.UnwrapErr());

        byte[] chainKey = chainKeyResult.Unwrap();

        try
        {
            Span<byte> currentChainKey = stackalloc byte[Constants.X25519KeySize];
            Span<byte> nextChainKey = stackalloc byte[Constants.X25519KeySize];
            Span<byte> msgKey = stackalloc byte[Constants.AesKeySize];

            chainKey.CopyTo(currentChainKey);

            for (uint idx = currentIndex + 1; idx <= targetIndex; idx++)
            {
                try
                {
                    HKDF.DeriveKey(
                        global::System.Security.Cryptography.HashAlgorithmName.SHA256,
                        ikm: currentChainKey,
                        output: msgKey,
                        salt: null,
                        info: Constants.MsgInfo
                    );

                    HKDF.DeriveKey(
                        global::System.Security.Cryptography.HashAlgorithmName.SHA256,
                        ikm: currentChainKey,
                        output: nextChainKey,
                        salt: null,
                        info: Constants.ChainInfo
                    );
                }
                catch (Exception ex)
                {
                    return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.DeriveKey($"HKDF failed during derivation at index {idx}.", ex));
                }

                Result<EcliptixMessageKey, EcliptixProtocolFailure>
                    keyResult = EcliptixMessageKey.New(idx, msgKey);

                if (keyResult.IsErr)
                    return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(keyResult.UnwrapErr());

                EcliptixMessageKey messageKey = keyResult.Unwrap();

                if (!_messageKeys.TryAdd(idx, messageKey))
                {
                    messageKey.Dispose();
                    return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(
                            $"Key for index {idx} unexpectedly appeared during derivation."));
                }

                Result<Unit, EcliptixProtocolFailure> writeResult =
                    _chainKeyHandle.Write(nextChainKey).MapSodiumFailure();
                if (writeResult.IsErr)
                {
                    _messageKeys.Remove(idx, out EcliptixMessageKey? removedKey);
                    removedKey?.Dispose();
                    return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
                }

                nextChainKey.CopyTo(currentChainKey);
            }

            Result<Unit, EcliptixProtocolFailure> setIndexResult = SetCurrentIndex(targetIndex);
            if (setIndexResult.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            PruneOldKeys();

            if (_messageKeys.TryGetValue(targetIndex, out EcliptixMessageKey? finalKey))
            {
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Ok(finalKey);
            }
            else
            {
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Derived key for index {targetIndex} missing after derivation loop."));
            }
        }
        finally
        {
            SodiumInterop.SecureWipe(chainKey);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> SkipKeysUntil(uint targetIndex)
    {
        if (_currentIndex >= targetIndex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        for (uint i = _currentIndex + 1; i <= targetIndex; i++)
        {
            Result<EcliptixMessageKey, EcliptixProtocolFailure> keyResult = GetOrDeriveKeyFor(i);
            if (keyResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(keyResult.UnwrapErr());
            }
        }

        return SetCurrentIndex(targetIndex);
    }

    internal SodiumSecureMemoryHandle? GetDhPrivateKeyHandle()
    {
        return _dhPrivateKeyHandle;
    }

    public Result<ChainStepState, EcliptixProtocolFailure> ToProtoState()
    {
        if (_disposed)
            return Result<ChainStepState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        try
        {
            byte[] chainKey = _chainKeyHandle.ReadBytes(Constants.X25519KeySize).Unwrap();
            byte[]? dhPrivKey = _dhPrivateKeyHandle?.ReadBytes(Constants.X25519PrivateKeySize).Unwrap();

            ChainStepState proto = new()
            {
                CurrentIndex = _currentIndex,
                ChainKey = ByteString.CopyFrom(chainKey),
            };

            if (dhPrivKey != null) proto.DhPrivateKey = ByteString.CopyFrom(dhPrivKey);
            if (_dhPublicKey != null) proto.DhPublicKey = ByteString.CopyFrom(_dhPublicKey);

            return Result<ChainStepState, EcliptixProtocolFailure>.Ok(proto);
        }
        catch (Exception ex)
        {
            return Result<ChainStepState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to export chain step to proto state.", ex));
        }
    }

    public static Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> FromProtoState(ChainStepType stepType,
        ChainStepState proto)
    {
        byte[]? chainKeyBytes = null;
        byte[]? dhPrivKeyBytes = null;
        byte[]? dhPubKeyBytes = null;

        try
        {
            SecureByteStringInterop.SecureCopyWithCleanup(proto.ChainKey, out chainKeyBytes);

            if (!proto.DhPrivateKey.IsEmpty)
                SecureByteStringInterop.SecureCopyWithCleanup(proto.DhPrivateKey, out dhPrivKeyBytes);

            if (!proto.DhPublicKey.IsEmpty)
                SecureByteStringInterop.SecureCopyWithCleanup(proto.DhPublicKey, out dhPubKeyBytes);

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> createResult =
                Create(stepType, chainKeyBytes!, dhPrivKeyBytes, dhPubKeyBytes);
            if (createResult.IsErr)
            {
                return createResult;
            }

            EcliptixProtocolChainStep chainStep = createResult.Unwrap();
            chainStep.SetCurrentIndex(proto.CurrentIndex)
                .Unwrap();

            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(chainStep);
        }
        finally
        {
            if (chainKeyBytes != null) SodiumInterop.SecureWipe(chainKeyBytes);
            if (dhPrivKeyBytes != null) SodiumInterop.SecureWipe(dhPrivKeyBytes);
            if (dhPubKeyBytes != null) SodiumInterop.SecureWipe(dhPubKeyBytes);
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> UpdateKeysAfterDhRatchet(byte[] newChainKey,
        byte[]? newDhPrivateKey = null,
        byte[]? newDhPublicKey = null)
    {
        Log.Information("🔧 CHAIN-STEP-UPDATE: Updating keys after DH ratchet - NewDhPrivateKey={HasPrivKey}, NewDhPublicKey={HasPubKey}(len={PubKeyLen})", 
            newDhPrivateKey != null, newDhPublicKey != null, newDhPublicKey?.Length ?? 0);
            
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return disposedCheck;

        Result<Unit, EcliptixProtocolFailure> keyValidation = ValidateNewChainKey(newChainKey);
        if (keyValidation.IsErr)
            return keyValidation;

        Result<Unit, EcliptixProtocolFailure> writeResult = _chainKeyHandle.Write(newChainKey).MapSodiumFailure();
        if (writeResult.IsErr)
            return writeResult;

        Result<Unit, EcliptixProtocolFailure> indexResult = SetCurrentIndex(0);
        if (indexResult.IsErr)
            return indexResult;

        Result<Unit, EcliptixProtocolFailure> dhUpdateResult = HandleDhKeyUpdate(newDhPrivateKey, newDhPublicKey);
        if (dhUpdateResult.IsErr)
            return dhUpdateResult;

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        if (_disposed)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateNewChainKey(byte[] newChainKey)
    {
        if (newChainKey.Length == Constants.X25519KeySize)
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        return Result<Unit, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.InvalidInput($"New chain key must be {Constants.X25519KeySize} bytes."));
    }

    private Result<Unit, EcliptixProtocolFailure> HandleDhKeyUpdate(byte[]? newDhPrivateKey, byte[]? newDhPublicKey)
    {
        _messageKeys.Clear();

        if (newDhPrivateKey == null && newDhPublicKey == null) return OkResult;

        Result<Unit, EcliptixProtocolFailure> validationResult = ValidateAll(
            () => ValidateDhKeysNotNull(newDhPrivateKey, newDhPublicKey),
            () => ValidateDhPrivateKeySize(newDhPrivateKey),
            () => ValidateDhPublicKeySize(newDhPublicKey)
        );
        if (validationResult.IsErr)
            return validationResult;

        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize);
        if (allocResult.IsErr)
            return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr().ToEcliptixProtocolFailure());

        SodiumSecureMemoryHandle handle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = handle.Write(newDhPrivateKey!.AsSpan());
        if (writeResult.IsErr)
        {
            handle.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        _dhPrivateKeyHandle?.Dispose();
        _dhPrivateKeyHandle = handle;

        if (_dhPublicKey != null) SodiumInterop.SecureWipe(_dhPublicKey);
        _dhPublicKey = (byte[])newDhPublicKey!.Clone();

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateAll(
        params Func<Result<Unit, EcliptixProtocolFailure>>[]? validators)
    {
        if (validators is null || validators.Length == 0) return OkResult;

        foreach (Func<Result<Unit, EcliptixProtocolFailure>> validate in validators)
        {
            Result<Unit, EcliptixProtocolFailure> result = validate();
            if (result.IsErr) return result;
        }

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhKeysNotNull(byte[]? privateKey, byte[]? publicKey)
    {
        if (privateKey == null && publicKey == null) return OkResult;

        if (privateKey == null || publicKey == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Both DH private and public keys must be provided together."));

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhPrivateKeySize(byte[]? privateKey)
    {
        if (privateKey == null) return OkResult;

        return privateKey.Length == Constants.X25519PrivateKeySize
            ? OkResult
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"DH private key must be {Constants.X25519PrivateKeySize} bytes."));
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhPublicKeySize(byte[]? publicKey)
    {
        if (publicKey == null) return OkResult;

        return publicKey.Length == Constants.X25519KeySize
            ? OkResult
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput($"DH public key must be {Constants.X25519KeySize} bytes."));
    }

    internal Result<byte[]?, EcliptixProtocolFailure> ReadDhPublicKey()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return Result<byte[]?, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

        byte[] result = (byte[])_dhPublicKey?.Clone()!;
        return Result<byte[]?, EcliptixProtocolFailure>.Ok(result);
    }

    internal void PruneOldKeys()
    {
        if (_disposed || _cacheWindow == 0 || _messageKeys.Count == 0) return;

        Result<uint, EcliptixProtocolFailure> currentIndexResult = GetCurrentIndex();
        if (currentIndexResult.IsErr) return;
        uint indexToPruneAgainst = currentIndexResult.Unwrap();

        uint minIndexToKeep = indexToPruneAgainst >= _cacheWindow ? indexToPruneAgainst - _cacheWindow + 1 : 0;

        List<uint> keysToRemove = [];
        keysToRemove.AddRange(_messageKeys.Keys.Where(key => key < minIndexToKeep));
        if (keysToRemove.Count == 0) return;
        foreach (uint keyIndex in keysToRemove)
        {
            if (_messageKeys.Remove(keyIndex, out EcliptixMessageKey? messageKeyToDispose))
            {
                messageKeyToDispose.Dispose();
            }
        }
    }
}