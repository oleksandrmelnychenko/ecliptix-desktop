using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Enums;
using Ecliptix.Protocol.System.Interfaces;
using Ecliptix.Protocol.System.Models.Keys;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Protocol.ChainStep;

internal sealed class EcliptixProtocolChainStep : IKeyProvider, IDisposable
{
    private const uint DEFAULT_CACHE_WINDOW_SIZE = ProtocolSystemConstants.ChainStep.DEFAULT_CACHE_WINDOW_SIZE;

    private static readonly Result<Unit, EcliptixProtocolFailure> OkResult =
        Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

    private readonly uint _cacheWindow;

    private readonly ChainStepType _stepType;

    private SodiumSecureMemoryHandle _chainKeyHandle;

    private readonly SortedDictionary<uint, SodiumSecureMemoryHandle> _messageKeys;

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
        _currentIndex = ProtocolSystemConstants.ChainStep.INITIAL_INDEX;
        _disposed = false;
        _messageKeys = [];
    }

    public Result<T, EcliptixProtocolFailure> ExecuteWithKey<T>(uint keyIndex,
        Func<ReadOnlySpan<byte>, Result<T, EcliptixProtocolFailure>> operation)
    {
        if (_disposed)
        {
            return Result<T, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        if (!_messageKeys.TryGetValue(keyIndex, out SodiumSecureMemoryHandle? keyHandle))
        {
            return Result<T, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.KEY_WITH_INDEX_NOT_FOUND, keyIndex)));
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (SodiumSecureMemoryHandle handle in _messageKeys.Values)
        {
            handle.Dispose();
        }

        _messageKeys.Clear();

        _chainKeyHandle.Dispose();
        _dhPrivateKeyHandle?.Dispose();
        if (_dhPublicKey != null)
        {
            SodiumInterop.SecureWipe(_dhPublicKey);
        }

        _chainKeyHandle = null!;
        _dhPrivateKeyHandle = null;
        _dhPublicKey = null;
    }

    public Result<uint, EcliptixProtocolFailure> GetCurrentIndex()
    {
        return _disposed
            ? Result<uint, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)))
            : Result<uint, EcliptixProtocolFailure>.Ok(_currentIndex);
    }

    internal Result<byte[], EcliptixProtocolFailure> GetCurrentChainKey()
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        return _chainKeyHandle.ReadBytes(Constants.X_25519_KEY_SIZE)
            .MapSodiumFailure();
    }

    internal Result<Unit, EcliptixProtocolFailure> SetCurrentIndex(uint value)
    {
        if (_disposed)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        _currentIndex = value;

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public static Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> Create(
        ChainStepType stepType,
        byte[] initialChainKey,
        byte[]? initialDhPrivateKey,
        byte[]? initialDhPublicKey,
        uint cacheWindowSize = DEFAULT_CACHE_WINDOW_SIZE)
    {
        Result<Unit, EcliptixProtocolFailure> keyValidation = ValidateInitialChainKey(initialChainKey);
        if (keyValidation.IsErr)
        {
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(keyValidation.UnwrapErr());
        }

        Result<(SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned), EcliptixProtocolFailure>
            dhKeysResult =
                ValidateAndPrepareDhKeys(initialDhPrivateKey, initialDhPublicKey);
        if (dhKeysResult.IsErr)
        {
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(dhKeysResult.UnwrapErr());
        }

        (SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned) dhInfo = dhKeysResult.Unwrap();

        Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> chainKeyResult =
            AllocateAndWriteChainKey(initialChainKey);
        if (chainKeyResult.IsErr)
        {
            dhInfo.dhPrivateKeyHandle?.Dispose();
            if (dhInfo.dhPublicKeyCloned != null)
            {
                SodiumInterop.SecureWipe(dhInfo.dhPublicKeyCloned);
            }

            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(chainKeyResult.UnwrapErr());
        }

        SodiumSecureMemoryHandle chainKeyHandle = chainKeyResult.Unwrap();
        uint actualCacheWindow = cacheWindowSize > ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD
            ? cacheWindowSize
            : DEFAULT_CACHE_WINDOW_SIZE;
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
        return initialChainKey.Length == Constants.X_25519_KEY_SIZE
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(string.Format(
                    EcliptixProtocolFailureMessages.ChainStep.INITIAL_CHAIN_KEY_INVALID_SIZE,
                    Constants.X_25519_KEY_SIZE)));
    }

    private static Result<(SodiumSecureMemoryHandle? dhPrivateKeyHandle, byte[]? dhPublicKeyCloned),
            EcliptixProtocolFailure>
        ValidateAndPrepareDhKeys(byte[]? initialDhPrivateKey, byte[]? initialDhPublicKey)
    {
        if (initialDhPrivateKey == null && initialDhPublicKey == null)
        {
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Ok((null, null));
        }

        if (initialDhPrivateKey == null || initialDhPublicKey == null)
        {
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.ChainStep
                    .DH_KEYS_PROVIDED_OR_NEITHER));
        }

        if (initialDhPrivateKey.Length != Constants.X_25519_PRIVATE_KEY_SIZE)
        {
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.INITIAL_DH_PRIVATE_KEY_INVALID_SIZE,
                        Constants.X_25519_PRIVATE_KEY_SIZE)));
        }

        if (initialDhPublicKey.Length != Constants.X_25519_KEY_SIZE)
        {
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.INITIAL_DH_PUBLIC_KEY_INVALID_SIZE,
                        Constants.X_25519_KEY_SIZE)));
        }

        SodiumSecureMemoryHandle? dhPrivateKeyHandle = null;
        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(Constants.X_25519_PRIVATE_KEY_SIZE);
            if (allocResult.IsErr)
            {
                return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                    allocResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            dhPrivateKeyHandle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = dhPrivateKeyHandle.Write(initialDhPrivateKey);
            if (writeResult.IsErr)
            {
                dhPrivateKeyHandle.Dispose();
                dhPrivateKeyHandle = null;
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
                EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.ChainStep.UNEXPECTED_ERROR_PREPARING_DH_KEYS, ex));
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> AllocateAndWriteChainKey(
        byte[] initialChainKey)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X_25519_KEY_SIZE);
        if (allocResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                allocResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        SodiumSecureMemoryHandle chainKeyHandle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = chainKeyHandle.Write(initialChainKey);
        if (!writeResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(chainKeyHandle);
        }

        chainKeyHandle.Dispose();
        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
            writeResult.UnwrapErr().ToEcliptixProtocolFailure());
    }

    internal Result<RatchetChainKey, EcliptixProtocolFailure> GetOrDeriveKeyFor(uint targetIndex)
    {
        if (_disposed)
        {
            return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        if (_messageKeys.ContainsKey(targetIndex))
        {
            return Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(new RatchetChainKey(targetIndex, this));
        }

        Result<uint, EcliptixProtocolFailure> currentIndexResult = GetCurrentIndex();
        if (currentIndexResult.IsErr)
        {
            return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());
        }

        uint currentIndex = currentIndexResult.Unwrap();

        if (targetIndex <= currentIndex)
        {
            return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.REQUESTED_INDEX_NOT_FUTURE, _stepType,
                        targetIndex, currentIndex)));
        }

        Result<byte[], EcliptixProtocolFailure> chainKeyResult = _chainKeyHandle.ReadBytes(Constants.X_25519_KEY_SIZE)
            .MapSodiumFailure();
        if (chainKeyResult.IsErr)
        {
            return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(chainKeyResult.UnwrapErr());
        }

        byte[] chainKey = chainKeyResult.Unwrap();

        try
        {
            Result<Unit, EcliptixProtocolFailure>
                deriveResult = DeriveKeysToTarget(chainKey, currentIndex, targetIndex);
            if (deriveResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(deriveResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> setIndexResult = SetCurrentIndex(targetIndex);
            if (setIndexResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());
            }

            PruneOldKeys();

            return _messageKeys.ContainsKey(targetIndex)
                ? Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(new RatchetChainKey(targetIndex, this))
                : Result<RatchetChainKey, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        string.Format(EcliptixProtocolFailureMessages.ChainStep.DERIVED_KEY_MISSING_AFTER_LOOP,
                            targetIndex)));
        }
        finally
        {
            SodiumInterop.SecureWipe(chainKey);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> DeriveKeysToTarget(byte[] chainKey, uint currentIndex,
        uint targetIndex)
    {
        Span<byte> currentChainKey = stackalloc byte[Constants.X_25519_KEY_SIZE];
        Span<byte> nextChainKey = stackalloc byte[Constants.X_25519_KEY_SIZE];
        Span<byte> msgKey = stackalloc byte[Constants.AES_KEY_SIZE];

        chainKey.CopyTo(currentChainKey);

        for (uint idx = currentIndex + ProtocolSystemConstants.ChainStep.INDEX_INCREMENT; idx <= targetIndex; idx++)
        {
            Result<Unit, EcliptixProtocolFailure> deriveResult =
                DeriveNextChainKeys(currentChainKey, nextChainKey, msgKey, idx);
            if (deriveResult.IsErr)
            {
                return deriveResult;
            }

            Result<Unit, EcliptixProtocolFailure> storeResult = StoreMessageKey(msgKey, nextChainKey, idx);
            if (storeResult.IsErr)
            {
                return storeResult;
            }

            nextChainKey.CopyTo(currentChainKey);
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> DeriveNextChainKeys(
        Span<byte> currentChainKey,
        Span<byte> nextChainKey,
        Span<byte> msgKey,
        uint idx)
    {
        try
        {
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: currentChainKey,
                output: msgKey,
                salt: null,
                info: Constants.MsgInfo
            );

            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: currentChainKey,
                output: nextChainKey,
                salt: null,
                info: Constants.ChainInfo
            );

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.HKDF_FAILED_DURING_DERIVATION, idx), ex));
        }
    }

    private Result<Unit, EcliptixProtocolFailure> StoreMessageKey(Span<byte> msgKey, Span<byte> nextChainKey, uint idx)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> secureHandleResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X_25519_KEY_SIZE);

        if (secureHandleResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.FAILED_TO_ALLOCATE_SECURE_MEMORY, idx)));
        }

        SodiumSecureMemoryHandle secureHandle = secureHandleResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = secureHandle.Write(msgKey);
        if (writeResult.IsErr)
        {
            secureHandle.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(
                writeResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        if (!_messageKeys.TryAdd(idx, secureHandle))
        {
            secureHandle.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.KEY_UNEXPECTEDLY_APPEARED, idx)));
        }

        Result<Unit, EcliptixProtocolFailure> chainWriteResult =
            _chainKeyHandle.Write(nextChainKey).MapSodiumFailure();
        if (chainWriteResult.IsErr)
        {
            _messageKeys.Remove(idx, out SodiumSecureMemoryHandle? removedHandle);
            removedHandle?.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(chainWriteResult.UnwrapErr());
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<Unit, EcliptixProtocolFailure> SkipKeysUntil(uint targetIndex)
    {
        if (_currentIndex >= targetIndex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        for (uint i = _currentIndex + ProtocolSystemConstants.ChainStep.INDEX_INCREMENT; i <= targetIndex; i++)
        {
            Result<RatchetChainKey, EcliptixProtocolFailure> keyResult = GetOrDeriveKeyFor(i);
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
        {
            return Result<ChainStepState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        try
        {
            byte[] chainKey = _chainKeyHandle.ReadBytes(Constants.X_25519_KEY_SIZE).Unwrap();
            byte[]? dhPrivKey = _dhPrivateKeyHandle?.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE).Unwrap();

            ChainStepState proto = new() { CurrentIndex = _currentIndex, ChainKey = ByteString.CopyFrom(chainKey), };

            if (dhPrivKey != null)
            {
                proto.DhPrivateKey = ByteString.CopyFrom(dhPrivKey);
            }

            if (_dhPublicKey != null)
            {
                proto.DhPublicKey = ByteString.CopyFrom(_dhPublicKey);
            }

            return Result<ChainStepState, EcliptixProtocolFailure>.Ok(proto);
        }
        catch (Exception ex)
        {
            return Result<ChainStepState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.ChainStep.FAILED_TO_EXPORT_CHAIN_STEP_STATE,
                    ex));
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
            {
                SecureByteStringInterop.SecureCopyWithCleanup(proto.DhPrivateKey, out dhPrivKeyBytes);
            }

            if (!proto.DhPublicKey.IsEmpty)
            {
                SecureByteStringInterop.SecureCopyWithCleanup(proto.DhPublicKey, out dhPubKeyBytes);
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> createResult =
                Create(stepType, chainKeyBytes, dhPrivKeyBytes, dhPubKeyBytes);
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
            SodiumInterop.SecureWipe(chainKeyBytes);

            if (dhPrivKeyBytes != null)
            {
                SodiumInterop.SecureWipe(dhPrivKeyBytes);
            }

            if (dhPubKeyBytes != null)
            {
                SodiumInterop.SecureWipe(dhPubKeyBytes);
            }
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> UpdateKeysAfterDhRatchet(byte[] newChainKey,
        byte[]? newDhPrivateKey = null,
        byte[]? newDhPublicKey = null)
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return disposedCheck;
        }

        Result<Unit, EcliptixProtocolFailure> keyValidation = ValidateNewChainKey(newChainKey);
        if (keyValidation.IsErr)
        {
            return keyValidation;
        }

        Result<Unit, EcliptixProtocolFailure> writeResult = _chainKeyHandle.Write(newChainKey).MapSodiumFailure();
        if (writeResult.IsErr)
        {
            return writeResult;
        }

        Result<Unit, EcliptixProtocolFailure> indexResult =
            SetCurrentIndex(ProtocolSystemConstants.ChainStep.RESET_INDEX);
        if (indexResult.IsErr)
        {
            return indexResult;
        }

        Result<Unit, EcliptixProtocolFailure> dhUpdateResult = HandleDhKeyUpdate(newDhPrivateKey, newDhPublicKey);
        return dhUpdateResult.IsErr ? dhUpdateResult : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        if (_disposed)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolChainStep)));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateNewChainKey(byte[] newChainKey)
    {
        if (newChainKey.Length == Constants.X_25519_KEY_SIZE)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        return Result<Unit, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.InvalidInput(
                string.Format(EcliptixProtocolFailureMessages.ChainStep.NEW_CHAIN_KEY_INVALID_SIZE,
                    Constants.X_25519_KEY_SIZE)));
    }

    private Result<Unit, EcliptixProtocolFailure> HandleDhKeyUpdate(byte[]? newDhPrivateKey, byte[]? newDhPublicKey)
    {
        _messageKeys.Clear();

        if (newDhPrivateKey == null && newDhPublicKey == null)
        {
            return OkResult;
        }

        Result<Unit, EcliptixProtocolFailure> validationResult = ValidateAll(
            () => ValidateDhKeysNotNull(newDhPrivateKey, newDhPublicKey),
            () => ValidateDhPrivateKeySize(newDhPrivateKey),
            () => ValidateDhPublicKeySize(newDhPublicKey)
        );
        if (validationResult.IsErr)
        {
            return validationResult;
        }

        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X_25519_PRIVATE_KEY_SIZE);
        if (allocResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        SodiumSecureMemoryHandle handle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = handle.Write(newDhPrivateKey!.AsSpan());
        if (writeResult.IsErr)
        {
            handle.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        _dhPrivateKeyHandle?.Dispose();
        _dhPrivateKeyHandle = handle;

        if (_dhPublicKey != null)
        {
            SodiumInterop.SecureWipe(_dhPublicKey);
        }

        _dhPublicKey = (byte[])newDhPublicKey!.Clone();

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateAll(
        params Func<Result<Unit, EcliptixProtocolFailure>>[]? validators)
    {
        if (validators is null ||
            validators.Length == ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD)
        {
            return OkResult;
        }

        foreach (Func<Result<Unit, EcliptixProtocolFailure>> validate in validators)
        {
            Result<Unit, EcliptixProtocolFailure> result = validate();
            if (result.IsErr)
            {
                return result;
            }
        }

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhKeysNotNull(byte[]? privateKey, byte[]? publicKey)
    {
        if (privateKey == null && publicKey == null)
        {
            return OkResult;
        }

        if (privateKey == null || publicKey == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    EcliptixProtocolFailureMessages.ChainStep.DH_KEYS_PROVIDED_TOGETHER));
        }

        return OkResult;
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhPrivateKeySize(byte[]? privateKey)
    {
        if (privateKey == null)
        {
            return OkResult;
        }

        return privateKey.Length == Constants.X_25519_PRIVATE_KEY_SIZE
            ? OkResult
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.ChainStep.DH_PRIVATE_KEY_INVALID_SIZE,
                        Constants.X_25519_PRIVATE_KEY_SIZE)));
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateDhPublicKeySize(byte[]? publicKey)
    {
        if (publicKey == null)
        {
            return OkResult;
        }

        return publicKey.Length == Constants.X_25519_KEY_SIZE
            ? OkResult
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(string.Format(
                    EcliptixProtocolFailureMessages.ChainStep.DH_PUBLIC_KEY_INVALID_SIZE, Constants.X_25519_KEY_SIZE)));
    }

    internal Result<Option<byte[]>, EcliptixProtocolFailure> ReadDhPublicKey()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return Result<Option<byte[]>, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
        }

        Option<byte[]> result = _dhPublicKey != null
            ? Option<byte[]>.Some((byte[])_dhPublicKey.Clone())
            : Option<byte[]>.None;

        return Result<Option<byte[]>, EcliptixProtocolFailure>.Ok(result);
    }

    internal void PruneOldKeys()
    {
        if (_disposed || _cacheWindow == ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD ||
            _messageKeys.Count == ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD)
        {
            return;
        }

        Result<uint, EcliptixProtocolFailure> currentIndexResult = GetCurrentIndex();
        if (currentIndexResult.IsErr)
        {
            return;
        }

        uint indexToPruneAgainst = currentIndexResult.Unwrap();

        uint minIndexToKeep = indexToPruneAgainst >= _cacheWindow
            ? indexToPruneAgainst - _cacheWindow + ProtocolSystemConstants.ChainStep.MIN_INDEX_TO_KEEP_OFFSET
            : ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD;

        List<uint> keysToRemove = [];
        keysToRemove.AddRange(_messageKeys.Keys.Where(key => key < minIndexToKeep));
        if (keysToRemove.Count == ProtocolSystemConstants.ChainStep.VALIDATOR_ARRAY_EMPTY_THRESHOLD)
        {
            return;
        }

        foreach (uint keyIndex in keysToRemove)
        {
            if (_messageKeys.Remove(keyIndex, out SodiumSecureMemoryHandle? removedHandle))
            {
                removedHandle.Dispose();
            }
        }
    }
}
