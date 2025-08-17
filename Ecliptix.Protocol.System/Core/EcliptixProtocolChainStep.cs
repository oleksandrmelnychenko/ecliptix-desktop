using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;

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

    private bool _isNewChain;

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
        _isNewChain = false;
        _disposed = false;
        _messageKeys = new SortedDictionary<uint, EcliptixMessageKey>();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _chainKeyHandle.Dispose();
        _dhPrivateKeyHandle?.Dispose();
        WipeIfNotNull(_dhPublicKey).IgnoreResult();

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
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            .Bind(_ => ValidateInitialChainKey(initialChainKey))
            .Bind(_ => ValidateAndPrepareDhKeys(initialDhPrivateKey, initialDhPublicKey))
            .Bind(dhInfo =>
                AllocateAndWriteChainKey(initialChainKey)
                    .Bind(chainKeyHandle =>
                    {
                        uint actualCacheWindow = cacheWindowSize > 0 ? cacheWindowSize : DefaultCacheWindowSize;
                        EcliptixProtocolChainStep step = new(
                            stepType,
                            chainKeyHandle,
                            dhInfo.dhPrivateKeyHandle,
                            dhInfo.dhPublicKeyCloned,
                            actualCacheWindow);

                        // Removed debug logging of sensitive chain key for security

                        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(step);
                    })
                    .MapErr(err =>
                    {
                        dhInfo.dhPrivateKeyHandle?.Dispose();
                        WipeIfNotNull(dhInfo.dhPublicKeyCloned).IgnoreResult();
                        return err;
                    })
            );
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
            return SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize).MapSodiumFailure()
                .Bind(handle =>
                {
                    dhPrivateKeyHandle = handle;
                    // Removed debug logging of sensitive DH private key for security
                    return handle.Write(initialDhPrivateKey).MapSodiumFailure();
                })
                .Map(_ =>
                {
                    byte[] dhPublicKeyCloned = (byte[])initialDhPublicKey.Clone();
                    // Removed debug logging of sensitive DH public key for security
                    return (dhPrivateKeyHandle, dhPublicKeyCloned);
                })
                .MapErr(err =>
                {
                    dhPrivateKeyHandle?.Dispose();
                    return err;
                })!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixProtocolChainStep] Unexpected error preparing DH keys: {ex.Message}");
            dhPrivateKeyHandle?.Dispose();
            return Result<(SodiumSecureMemoryHandle?, byte[]?), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Unexpected error preparing DH keys.", ex));
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> AllocateAndWriteChainKey(
        byte[] initialChainKey)
    {
        SodiumSecureMemoryHandle? chainKeyHandle = null;
        return SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure()
            .Bind(handle =>
            {
                chainKeyHandle = handle;
                return handle.Write(initialChainKey).MapSodiumFailure();
            })
            .Map(_ => chainKeyHandle!)
            .MapErr(err =>
            {
                chainKeyHandle?.Dispose();
                return err;
            });
    }

    internal Result<EcliptixMessageKey, EcliptixProtocolFailure> GetOrDeriveKeyFor(uint targetIndex)
    {
        if (_disposed)
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)));

        if (_messageKeys.TryGetValue(targetIndex, out EcliptixMessageKey? cachedKey))
        {
            Console.WriteLine($"[EcliptixProtocolChainStep] Retrieved cached message key for index {targetIndex}");
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
        Console.WriteLine($"[DESKTOP] Starting key derivation for {_stepType} from index {currentIndex + 1} to {targetIndex}");
        // Security: Never log cryptographic keys

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
                    using HkdfSha256 hkdfMsg = new(currentChainKey, null);
                    hkdfMsg.Expand(Constants.MsgInfo, msgKey);
                    Console.WriteLine($"[DESKTOP] Derived message key for {_stepType} index {idx} (hidden for security)");

                    using HkdfSha256 hkdfChain = new(currentChainKey, null);
                    hkdfChain.Expand(Constants.ChainInfo, nextChainKey);
                    // Security: Never log chain keys
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

                // Removed debug logging of sensitive chain key for security
            }

            Result<Unit, EcliptixProtocolFailure> setIndexResult = SetCurrentIndex(targetIndex);
            if (setIndexResult.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            PruneOldKeys();

            // Removed debug logging of sensitive message keys cache for security

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
            WipeIfNotNull(chainKey).IgnoreResult();
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

    internal SodiumSecureMemoryHandle? GetDhPrivateKeyHandle() => _dhPrivateKeyHandle;

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

            // Removed debug logging of sensitive cryptographic keys for security

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
        byte[]? chainKeyBytes = proto.ChainKey.ToByteArray();
        byte[]? dhPrivKeyBytes = proto.DhPrivateKey.ToByteArray();
        byte[]? dhPubKeyBytes = proto.DhPublicKey.ToByteArray();

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

    internal Result<Unit, EcliptixProtocolFailure> UpdateKeysAfterDhRatchet(byte[] newChainKey,
        byte[]? newDhPrivateKey = null,
        byte[]? newDhPublicKey = null)
    {
        Console.WriteLine($"[DESKTOP] UpdateKeysAfterDhRatchet called for {_stepType}");
        // Security: Never log cryptographic keys
        if (newDhPublicKey != null)
            Console.WriteLine($"[DESKTOP] New DH public key provided (hidden for security)");

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            .Bind(_ => CheckDisposed())
            .Bind(_ => ValidateNewChainKey(newChainKey))
            .Bind(_ => _chainKeyHandle.Write(newChainKey).MapSodiumFailure())
            .Bind(_ => SetCurrentIndex(0))
            .Bind(_ => HandleDhKeyUpdate(newDhPrivateKey, newDhPublicKey))
            .Map(_ =>
            {
                _isNewChain = _stepType == ChainStepType.Sender;
                Console.WriteLine($"[DESKTOP] UpdateKeysAfterDhRatchet completed for {_stepType}");
                return Unit.Value;
            });
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed() =>
        _disposed
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolChainStep)))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

    private static Result<Unit, EcliptixProtocolFailure> ValidateNewChainKey(byte[] newChainKey) =>
        newChainKey.Length == Constants.X25519KeySize
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput($"New chain key must be {Constants.X25519KeySize} bytes."));

    private Result<Unit, EcliptixProtocolFailure> HandleDhKeyUpdate(byte[]? newDhPrivateKey, byte[]? newDhPublicKey)
    {
        _messageKeys.Clear();

        if (newDhPrivateKey == null && newDhPublicKey == null) return OkResult;

        return ValidateAll(
            () => ValidateDhKeysNotNull(newDhPrivateKey, newDhPublicKey),
            () => ValidateDhPrivateKeySize(newDhPrivateKey),
            () => ValidateDhPublicKeySize(newDhPublicKey)
        ).Bind(_ =>
        {
            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> newHandleResult =
                SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize)
                    .MapSodiumFailure()
                    .Bind(handle => handle.Write(newDhPrivateKey!.AsSpan())
                        .MapSodiumFailure()
                        .Map(_ => handle)
                        .MapErr(err =>
                        {
                            handle.Dispose();
                            return err;
                        }));

            if (newHandleResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(newHandleResult.UnwrapErr());
            }

            SodiumSecureMemoryHandle newDhPrivateKeyHandle = newHandleResult.Unwrap();

            _dhPrivateKeyHandle?.Dispose();
            _dhPrivateKeyHandle = newDhPrivateKeyHandle;

            WipeIfNotNull(_dhPublicKey).IgnoreResult();
            _dhPublicKey = (byte[])newDhPublicKey!.Clone();

            // Removed debug logging of sensitive DH keys for security

            return OkResult;
        });
    }

    /*private Result<Unit, EcliptixProtocolFailure> EnsureDhPrivateKeyHandle()
    {
        if (_dhPrivateKeyHandle != null) return OkResult;

        Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X25519PrivateKeySize).MapSodiumFailure();
        if (allocResult.IsErr) return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());

        _dhPrivateKeyHandle = allocResult.Unwrap();
        return OkResult;
    }*/

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
        return CheckDisposed().Map<byte[]?>(_ =>
        {
            byte[] result = (byte[])_dhPublicKey?.Clone()!;
            return result;
        });
    }

    internal void PruneOldKeys()
    {
        if (_disposed || _cacheWindow == 0 || _messageKeys.Count == 0) return;

        Result<uint, EcliptixProtocolFailure> currentIndexResult = GetCurrentIndex();
        if (currentIndexResult.IsErr) return;
        uint indexToPruneAgainst = currentIndexResult.Unwrap();

        uint minIndexToKeep = indexToPruneAgainst >= _cacheWindow ? indexToPruneAgainst - _cacheWindow + 1 : 0;

        List<uint> keysToRemove = _messageKeys.Keys.Where(k => k < minIndexToKeep).ToList();
        if (keysToRemove.Count != 0)
        {
            foreach (uint keyIndex in keysToRemove)
            {
                if (_messageKeys.Remove(keyIndex, out EcliptixMessageKey? messageKeyToDispose))
                {
                    messageKeyToDispose.Dispose();
                }
            }
        }

        // Removed debug logging of sensitive message keys cache for security
    }

    private static Result<Unit, EcliptixProtocolFailure> WipeIfNotNull(byte[]? data) =>
        data == null
            ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
            : SodiumInterop.SecureWipe(data).MapSodiumFailure();
}