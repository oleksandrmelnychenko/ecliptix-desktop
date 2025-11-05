using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Models.Bundles;
using Ecliptix.Protocol.System.Models.KeyMaterials;
using Ecliptix.Protocol.System.Models.Keys;
using Ecliptix.Protocol.System.Security.KeyDerivation;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Sodium;

namespace Ecliptix.Protocol.System.Identity;

public sealed class EcliptixSystemIdentityKeys : IDisposable
{
    private readonly byte[] _ed25519PublicKey;
    private readonly SodiumSecureMemoryHandle _ed25519SecretKeyHandle;
    private readonly SodiumSecureMemoryHandle _identityX25519SecretKeyHandle;
    private readonly byte[] _identityX25519PublicKey;
    private readonly uint _signedPreKeyId;
    private readonly byte[] _signedPreKeyPublic;
    private readonly SodiumSecureMemoryHandle _signedPreKeySecretKeyHandle;
    private readonly byte[] _signedPreKeySignature;
    private bool _disposed;
    private SodiumSecureMemoryHandle? _ephemeralSecretKeyHandle;
    private byte[]? _ephemeralX25519PublicKey;
    private List<OneTimePreKeyLocal> _oneTimePreKeysInternal;

    private EcliptixSystemIdentityKeys(IdentityKeysMaterial material)
    {
        _ed25519SecretKeyHandle = material.Ed25519.SecretKeyHandle;
        _ed25519PublicKey = material.Ed25519.PublicKey;
        _identityX25519SecretKeyHandle = material.IdentityX25519.SecretKeyHandle;
        _identityX25519PublicKey = material.IdentityX25519.PublicKey;
        _signedPreKeyId = material.SignedPreKey.Id;
        _signedPreKeySecretKeyHandle = material.SignedPreKey.SecretKeyHandle;
        _signedPreKeyPublic = material.SignedPreKey.PublicKey;
        _signedPreKeySignature = material.SignedPreKey.Signature;
        _oneTimePreKeysInternal = material.OneTimePreKeys;
        _disposed = false;
    }

    private ReadOnlySpan<byte> IdentityX25519PublicKeySpan => _identityX25519PublicKey;

    public byte[] GetIdentityX25519PublicKeyCopy() => (byte[])_identityX25519PublicKey.Clone();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Result<IdentityKeysState, EcliptixProtocolFailure> ToProtoState()
    {
        if (_disposed)
        {
            return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixSystemIdentityKeys)));
        }

        try
        {
            List<OneTimePreKeySecret> opkProtos = [];
            foreach (OneTimePreKeyLocal opk in _oneTimePreKeysInternal)
            {
                Result<ByteString, SodiumFailure> privateKeyResult =
                    SecureByteStringInterop.CreateByteStringFromSecureMemory(opk.PrivateKeyHandle,
                        Constants.X_25519_PRIVATE_KEY_SIZE);
                if (privateKeyResult.IsErr)
                {
                    return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(
                            string.Format(EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_READ_OPK_PRIVATE_KEY,
                                privateKeyResult.UnwrapErr().Message)));
                }

                opkProtos.Add(new OneTimePreKeySecret
                {
                    PreKeyId = opk.PreKeyId,
                    PrivateKey = privateKeyResult.Unwrap(),
                    PublicKey = SecureByteStringInterop.CreateByteStringFromSpan(opk.PublicKeySpan)
                });
            }

            Result<ByteString, SodiumFailure> ed25519SecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_ed25519SecretKeyHandle,
                    Constants.ED_25519_SECRET_KEY_SIZE);
            if (ed25519SecretResult.IsErr)
            {
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        string.Format(EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_READ_ED_25519_SECRET_KEY,
                            ed25519SecretResult.UnwrapErr().Message)));
            }

            Result<ByteString, SodiumFailure> identityX25519SecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_identityX25519SecretKeyHandle,
                    Constants.X_25519_PRIVATE_KEY_SIZE);
            if (identityX25519SecretResult.IsErr)
            {
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        string.Format(
                            EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_READ_IDENTITY_X_25519_SECRET_KEY,
                            identityX25519SecretResult.UnwrapErr().Message)));
            }

            Result<ByteString, SodiumFailure> signedPreKeySecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_signedPreKeySecretKeyHandle,
                    Constants.X_25519_PRIVATE_KEY_SIZE);
            if (signedPreKeySecretResult.IsErr)
            {
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        string.Format(EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_READ_SIGNED_PRE_KEY_SECRET,
                            signedPreKeySecretResult.UnwrapErr().Message)));
            }

            IdentityKeysState proto = new()
            {
                Ed25519SecretKey = ed25519SecretResult.Unwrap(),
                IdentityX25519SecretKey = identityX25519SecretResult.Unwrap(),
                SignedPreKeySecret = signedPreKeySecretResult.Unwrap(),
                Ed25519PublicKey = SecureByteStringInterop.CreateByteStringFromSpan(_ed25519PublicKey),
                IdentityX25519PublicKey =
                    SecureByteStringInterop.CreateByteStringFromSpan(IdentityX25519PublicKeySpan),
                SignedPreKeyId = _signedPreKeyId,
                SignedPreKeyPublic = SecureByteStringInterop.CreateByteStringFromSpan(_signedPreKeyPublic),
                SignedPreKeySignature = SecureByteStringInterop.CreateByteStringFromSpan(_signedPreKeySignature)
            };
            proto.OneTimePreKeys.AddRange(opkProtos);

            return Result<IdentityKeysState, EcliptixProtocolFailure>.Ok(proto);
        }
        catch (Exception ex)
        {
            return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_EXPORT_IDENTITY_KEYS_TO_PROTO, ex));
        }
    }

    public static Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> FromProtoState(IdentityKeysState proto)
    {
        SodiumSecureMemoryHandle? edSkHandle = null;
        SodiumSecureMemoryHandle? idXSkHandle = null;
        SodiumSecureMemoryHandle? spkSkHandle = null;
        List<OneTimePreKeyLocal>? opks = [];

        List<byte[]> opkPkBytesList = [];

        try
        {
            edSkHandle = SodiumSecureMemoryHandle.Allocate(proto.Ed25519SecretKey.Length).Unwrap();
            SecureByteStringInterop.CopyFromByteStringToSecureMemory(proto.Ed25519SecretKey, edSkHandle).Unwrap();

            idXSkHandle = SodiumSecureMemoryHandle.Allocate(proto.IdentityX25519SecretKey.Length).Unwrap();
            SecureByteStringInterop.CopyFromByteStringToSecureMemory(proto.IdentityX25519SecretKey, idXSkHandle)
                .Unwrap();

            spkSkHandle = SodiumSecureMemoryHandle.Allocate(proto.SignedPreKeySecret.Length).Unwrap();
            SecureByteStringInterop.CopyFromByteStringToSecureMemory(proto.SignedPreKeySecret, spkSkHandle).Unwrap();

            SecureByteStringInterop.SecureCopyWithCleanup(proto.Ed25519PublicKey, out byte[] edPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityX25519PublicKey, out byte[] idXPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeyPublic, out byte[] spkPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeySignature, out byte[] spkSig);

            foreach (OneTimePreKeySecret opkProto in proto.OneTimePreKeys)
            {
                SodiumSecureMemoryHandle skHandle =
                    SodiumSecureMemoryHandle.Allocate(opkProto.PrivateKey.Length).Unwrap();
                SecureByteStringInterop.CopyFromByteStringToSecureMemory(opkProto.PrivateKey, skHandle).Unwrap();

                SecureByteStringInterop.SecureCopyWithCleanup(opkProto.PublicKey, out byte[] opkPkBytes);
                opkPkBytesList.Add(opkPkBytes);

                OneTimePreKeyLocal opk = OneTimePreKeyLocal.CreateFromParts(opkProto.PreKeyId, skHandle, opkPkBytes);
                opks.Add(opk);
            }

            IdentityKeysMaterial material = new(
                new Ed25519KeyMaterial(edSkHandle, edPk),
                new X25519KeyMaterial(idXSkHandle, idXPk),
                new SignedPreKeyMaterial(proto.SignedPreKeyId, spkSkHandle, spkPk, spkSig),
                opks);

            EcliptixSystemIdentityKeys keys = new(material);

            edSkHandle = null;
            idXSkHandle = null;
            spkSkHandle = null;
            opks = null;

            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(keys);
        }
        catch (Exception ex)
        {
            CleanupKeyHandlesAndOpks(edSkHandle, idXSkHandle, spkSkHandle, opks);
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_REHYDRATE_FROM_PROTO, ex));
        }
        finally
        {
            foreach (byte[] pkBytes in opkPkBytesList)
            {
                SodiumInterop.SecureWipe(pkBytes);
            }
        }
    }

    public static Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> Create(uint oneTimeKeyCount)
    {
        if (oneTimeKeyCount > int.MaxValue)
        {
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.IdentityKeys
                    .ONE_TIME_KEY_COUNT_EXCEEDS_LIMITS));
        }

        SodiumSecureMemoryHandle? edSkHandle = null;
        SodiumSecureMemoryHandle? idXSkHandle = null;
        SodiumSecureMemoryHandle? spkSkHandle = null;
        List<OneTimePreKeyLocal>? opks = null;

        try
        {
            Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> edKeysResult =
                GenerateEd25519Keys();
            if (edKeysResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(edKeysResult.UnwrapErr());
            }

            (edSkHandle, byte[] edPk) = edKeysResult.Unwrap();

            Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> idKeysResult =
                GenerateX25519IdentityKeys();
            if (idKeysResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());
            }

            (idXSkHandle, byte[] idXPk) = idKeysResult.Unwrap();

            uint spkId = Helpers.GenerateRandomUInt32();
            Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> spkKeysResult =
                GenerateX25519SignedPreKey(spkId);
            if (spkKeysResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(spkKeysResult.UnwrapErr());
            }

            (spkSkHandle, byte[] spkPk) = spkKeysResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> signatureResult = SignSignedPreKey(edSkHandle, spkPk);
            if (signatureResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());
            }

            byte[] spkSig = signatureResult.Unwrap();

            Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure> opksResult =
                GenerateOneTimePreKeys(oneTimeKeyCount);
            if (opksResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(opksResult.UnwrapErr());
            }

            opks = opksResult.Unwrap();

            IdentityKeysMaterial keysMaterial = new(
                new Ed25519KeyMaterial(edSkHandle, edPk),
                new X25519KeyMaterial(idXSkHandle, idXPk),
                new SignedPreKeyMaterial(spkId, spkSkHandle, spkPk, spkSig),
                opks);

            EcliptixSystemIdentityKeys keys = new(keysMaterial);

            edSkHandle = null;
            idXSkHandle = null;
            spkSkHandle = null;
            opks = null;

            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(keys);
        }
        catch (Exception ex)
        {
            CleanupKeyHandlesAndOpks(edSkHandle, idXSkHandle, spkSkHandle, opks);
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    string.Format(
                        EcliptixProtocolFailureMessages.IdentityKeys.UNEXPECTED_ERROR_INITIALIZING_LOCAL_KEY_MATERIAL,
                        ex.Message), ex));
        }
    }

    public static Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>
        CreateFromMasterKey(byte[] masterKey, string membershipId, uint oneTimeKeyCount)
    {
        SodiumSecureMemoryHandle? edSkHandle = null;
        SodiumSecureMemoryHandle? idXSkHandle = null;
        SodiumSecureMemoryHandle? spkSkHandle = null;
        List<OneTimePreKeyLocal>? opks = null;
        byte[]? ed25519Seed = null;
        byte[]? x25519Seed = null;

        try
        {
            ed25519Seed = MasterKeyDerivation.DeriveEd25519Seed(masterKey, membershipId);
            KeyPair edKeyPair = PublicKeyAuth.GenerateKeyPair(ed25519Seed);

            edSkHandle = SodiumSecureMemoryHandle.Allocate(Constants.ED_25519_SECRET_KEY_SIZE).Unwrap();
            edSkHandle.Write(edKeyPair.PrivateKey).Unwrap();
            SodiumInterop.SecureWipe(edKeyPair.PrivateKey);

            x25519Seed = MasterKeyDerivation.DeriveX25519Seed(masterKey, membershipId);
            idXSkHandle = SodiumSecureMemoryHandle.Allocate(Constants.X_25519_PRIVATE_KEY_SIZE).Unwrap();
            idXSkHandle.Write(x25519Seed).Unwrap();

            byte[] x25519PublicKey = ScalarMult.Base(x25519Seed);

            byte[] spkSeed = MasterKeyDerivation.DeriveSignedPreKeySeed(masterKey, membershipId);
            uint spkId = BitConverter.ToUInt32(spkSeed, 0);
            byte[] spkPrivateKey = new byte[Constants.X_25519_PRIVATE_KEY_SIZE];
            Array.Copy(spkSeed, 0, spkPrivateKey, 0, Constants.X_25519_PRIVATE_KEY_SIZE);
            byte[] spkPk = ScalarMult.Base(spkPrivateKey);

            spkSkHandle = SodiumSecureMemoryHandle.Allocate(Constants.X_25519_PRIVATE_KEY_SIZE).Unwrap();
            spkSkHandle.Write(spkPrivateKey).Unwrap();
            SodiumInterop.SecureWipe(spkPrivateKey);
            SodiumInterop.SecureWipe(spkSeed);

            Result<byte[], EcliptixProtocolFailure> signatureResult = SignSignedPreKey(edSkHandle, spkPk);
            if (signatureResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());
            }

            byte[] spkSig = signatureResult.Unwrap();

            Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure> opksResult =
                GenerateOneTimePreKeys(oneTimeKeyCount);
            if (opksResult.IsErr)
            {
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(opksResult.UnwrapErr());
            }

            opks = opksResult.Unwrap();

            IdentityKeysMaterial keysMaterial = new(
                new Ed25519KeyMaterial(edSkHandle, edKeyPair.PublicKey),
                new X25519KeyMaterial(idXSkHandle, x25519PublicKey),
                new SignedPreKeyMaterial(spkId, spkSkHandle, spkPk, spkSig),
                opks);

            EcliptixSystemIdentityKeys keys = new(keysMaterial);

            edSkHandle = null;
            idXSkHandle = null;
            spkSkHandle = null;
            opks = null;

            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(keys);
        }
        finally
        {
            if (ed25519Seed != null)
            {
                SodiumInterop.SecureWipe(ed25519Seed);
            }

            if (x25519Seed != null)
            {
                SodiumInterop.SecureWipe(x25519Seed);
            }

            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            if (opks != null)
            {
                foreach (OneTimePreKeyLocal opk in opks)
                {
                    opk.Dispose();
                }
            }
        }
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> GenerateEd25519Keys()
    {
        SodiumSecureMemoryHandle? skHandle;
        byte[]? skBytes = null;
        try
        {
            return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Try(() =>
                {
                    KeyPair edKeyPair = PublicKeyAuth.GenerateKeyPair();
                    skBytes = edKeyPair.PrivateKey;
                    byte[] pkBytes = edKeyPair.PublicKey;

                    skHandle = SodiumSecureMemoryHandle.Allocate(Constants.ED_25519_SECRET_KEY_SIZE).Unwrap();
                    skHandle.Write(skBytes).Unwrap();

                    return (skHandle, pkBytes);
                },
                ex => EcliptixProtocolFailure.KeyGeneration(
                    EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_GENERATE_ED_25519_KEY_PAIR, ex));
        }
        finally
        {
            if (skBytes != null)
            {
                SodiumInterop.SecureWipe(skBytes);
            }
        }
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure>
        GenerateX25519IdentityKeys()
    {
        return SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.IdentityKeys
            .IDENTITY_KEY_PAIR_PURPOSE);
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure>
        GenerateX25519SignedPreKey(uint id)
    {
        return SodiumInterop.GenerateX25519KeyPair(
            string.Format(EcliptixProtocolFailureMessages.IdentityKeys.SIGNED_PRE_KEY_PAIR_PURPOSE, id));
    }

    private static Result<byte[], EcliptixProtocolFailure> SignSignedPreKey(SodiumSecureMemoryHandle edSkHandle,
        byte[] spkPk)
    {
        byte[]? tempEdSignKeyCopy = null;
        try
        {
            Result<byte[], EcliptixProtocolFailure> readResult =
                edSkHandle.ReadBytes(Constants.ED_25519_SECRET_KEY_SIZE).MapSodiumFailure();
            if (readResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());
            }

            tempEdSignKeyCopy = readResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> signResult = Result<byte[], EcliptixProtocolFailure>.Try(
                () => PublicKeyAuth.SignDetached(spkPk, tempEdSignKeyCopy),
                ex => EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_SIGN_PRE_KEY_PUBLIC_KEY, ex));

            if (signResult.IsErr)
            {
                return signResult;
            }

            byte[] signature = signResult.Unwrap();
            if (signature.Length == Constants.ED_25519_SIGNATURE_SIZE)
            {
                return Result<byte[], EcliptixProtocolFailure>.Ok(signature);
            }

            SodiumInterop.SecureWipe(signature);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    string.Format(EcliptixProtocolFailureMessages.IdentityKeys.GENERATED_SPK_SIGNATURE_INCORRECT_SIZE,
                        signature.Length)));
        }
        finally
        {
            if (tempEdSignKeyCopy != null)
            {
                SodiumInterop.SecureWipe(tempEdSignKeyCopy);
            }
        }
    }

    private static Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure> GenerateOneTimePreKeys(uint count)
    {
        if (count == 0)
        {
            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Ok([]);
        }

        List<OneTimePreKeyLocal> opks = new((int)count);
        HashSet<uint> usedIds = new((int)count);
        uint idCounter = 2;

        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = idCounter++;
                while (usedIds.Contains(id))
                {
                    id = Helpers.GenerateRandomUInt32();
                }

                usedIds.Add(id);

                Result<OneTimePreKeyLocal, EcliptixProtocolFailure> opkResult = OneTimePreKeyLocal.Generate(id);
                if (opkResult.IsErr)
                {
                    foreach (OneTimePreKeyLocal generatedOpk in opks)
                    {
                        generatedOpk.Dispose();
                    }

                    return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Err(opkResult.UnwrapErr());
                }

                OneTimePreKeyLocal opk = opkResult.Unwrap();

                opks.Add(opk);
            }

            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Ok(opks);
        }
        catch (Exception ex)
        {
            foreach (OneTimePreKeyLocal generatedOpk in opks)
            {
                generatedOpk.Dispose();
            }

            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(
                    EcliptixProtocolFailureMessages.IdentityKeys.UNEXPECTED_ERROR_GENERATING_ONE_TIME_PREKEYS, ex));
        }
    }


    internal Result<LocalPublicKeyBundle, EcliptixProtocolFailure> CreatePublicBundle()
    {
        if (_disposed)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixSystemIdentityKeys)));
        }

        try
        {
            List<OneTimePreKeyRecord> opkRecords = [];
            opkRecords.AddRange(_oneTimePreKeysInternal.Select(opkLocal =>
                new OneTimePreKeyRecord(opkLocal.PreKeyId, opkLocal.GetPublicKeyCopy())));

            LocalPublicKeyBundle bundle = new(
                _ed25519PublicKey,
                GetIdentityX25519PublicKeyCopy(),
                _signedPreKeyId,
                _signedPreKeyPublic,
                _signedPreKeySignature,
                opkRecords,
                _ephemeralX25519PublicKey);

            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Ok(bundle);
        }
        catch (Exception ex)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    EcliptixProtocolFailureMessages.IdentityKeys.FAILED_TO_CREATE_PUBLIC_KEY_BUNDLE, ex));
        }
    }

    public void GenerateEphemeralKeyPair()
    {
        if (_disposed)
        {
            Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixSystemIdentityKeys)));
            return;
        }

        _ephemeralSecretKeyHandle?.Dispose();
        if (_ephemeralX25519PublicKey != null)
        {
            SodiumInterop.SecureWipe(_ephemeralX25519PublicKey);
        }

        Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> generationResult =
            SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.IdentityKeys
                .EPHEMERAL_KEY_PAIR_PURPOSE);

        if (generationResult.IsOk)
        {
            (SodiumSecureMemoryHandle skHandle, byte[] pk) keys = generationResult.Unwrap();
            _ephemeralSecretKeyHandle = keys.skHandle;
            _ephemeralX25519PublicKey = keys.pk;
        }
        else
        {
            _ephemeralSecretKeyHandle = null;
            _ephemeralX25519PublicKey = null;
        }
    }

    private Result<Unit, EcliptixProtocolFailure> ValidateX3dhPrerequisites(
        LocalPublicKeyBundle remoteBundle, ReadOnlySpan<byte> info)
    {
        Result<Unit, EcliptixProtocolFailure> hkdfInfoValidationResult = ValidateHkdfInfo(info);
        if (hkdfInfoValidationResult.IsErr)
        {
            return hkdfInfoValidationResult;
        }

        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return disposedCheck;
        }

        Result<Unit, EcliptixProtocolFailure> bundleValidation = ValidateRemoteBundle(remoteBundle);
        return bundleValidation.IsErr ? bundleValidation : EnsureLocalKeysValid();
    }

    private static int PerformX3dhDiffieHellman(
        Span<byte> ephemeralSecretSpan, Span<byte> identitySecretSpan,
        LocalPublicKeyBundle remoteBundle, bool useOpk, Span<byte> dhResultsSpan)
    {
        byte[]? ephemeralSecretArray = null;
        byte[]? identitySecretArray = null;
        byte[]? dh1 = null;
        byte[]? dh2 = null;
        byte[]? dh3 = null;
        int dhOffset;

        try
        {
            ephemeralSecretArray = ephemeralSecretSpan.ToArray();
            identitySecretArray = identitySecretSpan.ToArray();

            dh1 = ScalarMult.Mult(identitySecretArray, remoteBundle.SignedPreKeyPublic);
            dh2 = ScalarMult.Mult(ephemeralSecretArray, remoteBundle.IdentityX25519);
            dh3 = ScalarMult.Mult(ephemeralSecretArray, remoteBundle.SignedPreKeyPublic);

            dh1.AsSpan().CopyTo(dhResultsSpan[..Constants.X_25519_KEY_SIZE]);
            dhOffset = Constants.X_25519_KEY_SIZE;
            dh2.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X_25519_KEY_SIZE)]);
            dhOffset += Constants.X_25519_KEY_SIZE;
            dh3.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X_25519_KEY_SIZE)]);
            dhOffset += Constants.X_25519_KEY_SIZE;

            if (useOpk && remoteBundle.OneTimePreKeys.Count > 0)
            {
                byte[]? dh4 = null;
                try
                {
                    dh4 = ScalarMult.Mult(ephemeralSecretArray, remoteBundle.OneTimePreKeys[0].PublicKey);
                    dh4.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X_25519_KEY_SIZE)]);
                    dhOffset += Constants.X_25519_KEY_SIZE;
                }
                finally
                {
                    if (dh4 != null)
                    {
                        SodiumInterop.SecureWipe(dh4);
                    }
                }
            }

            return dhOffset;
        }
        finally
        {
            if (ephemeralSecretArray != null)
            {
                SodiumInterop.SecureWipe(ephemeralSecretArray);
            }

            if (identitySecretArray != null)
            {
                SodiumInterop.SecureWipe(identitySecretArray);
            }

            SecureWipeDhResults(dh1, dh2, dh3);
        }
    }

    internal Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> X3dhDeriveSharedSecret(
        LocalPublicKeyBundle remoteBundle, ReadOnlySpan<byte> info)
    {
        SodiumSecureMemoryHandle? ephemeralHandleUsed = null;
        SodiumSecureMemoryHandle? secureOutputHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> validationResult = ValidateX3dhPrerequisites(remoteBundle, info);
            if (validationResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(validationResult.UnwrapErr());
            }

            byte[] infoArray = info.ToArray();

            return SecureMemoryUtils.WithSecureBuffers(
                [
                    Constants.X_25519_PRIVATE_KEY_SIZE, Constants.X_25519_PRIVATE_KEY_SIZE,
                    Constants.X_25519_KEY_SIZE * 4,
                    Constants.X_25519_KEY_SIZE * 5, Constants.X_25519_KEY_SIZE
                ],
                buffers =>
                {
                    Span<byte> ephemeralSecretSpan = buffers[0].GetSpan()[..Constants.X_25519_PRIVATE_KEY_SIZE];
                    Span<byte> identitySecretSpan = buffers[1].GetSpan()[..Constants.X_25519_PRIVATE_KEY_SIZE];
                    Span<byte> dhResultsSpan = buffers[2].GetSpan()[..(Constants.X_25519_KEY_SIZE * 4)];
                    Span<byte> ikmSpan = buffers[3].GetSpan()[..(Constants.X_25519_KEY_SIZE * 5)];
                    Span<byte> hkdfOutputSpan = buffers[4].GetSpan()[..Constants.X_25519_KEY_SIZE];

                    Result<byte[], EcliptixProtocolFailure> readEphResult = _ephemeralSecretKeyHandle!
                        .ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE).MapSodiumFailure();
                    if (readEphResult.IsErr)
                    {
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readEphResult.UnwrapErr());
                    }

                    byte[] ephemeralSecretBytes = readEphResult.Unwrap();
                    ephemeralSecretBytes.CopyTo(ephemeralSecretSpan);
                    SodiumInterop.SecureWipe(ephemeralSecretBytes);

                    Result<byte[], EcliptixProtocolFailure> readIdResult = _identityX25519SecretKeyHandle
                        .ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE).MapSodiumFailure();
                    if (readIdResult.IsErr)
                    {
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readIdResult.UnwrapErr());
                    }

                    byte[] identitySecretBytes = readIdResult.Unwrap();
                    identitySecretBytes.CopyTo(identitySecretSpan);
                    SodiumInterop.SecureWipe(identitySecretBytes);

                    ephemeralHandleUsed = _ephemeralSecretKeyHandle;
                    _ephemeralSecretKeyHandle = null;

                    bool useOpk = remoteBundle.OneTimePreKeys.FirstOrDefault()?.PublicKey is
                    { Length: Constants.X_25519_PUBLIC_KEY_SIZE };

                    int dhOffset = PerformX3dhDiffieHellman(
                        ephemeralSecretSpan, identitySecretSpan,
                        remoteBundle, useOpk, dhResultsSpan);

                    Span<byte> ikmBuildSpan = ikmSpan[..(Constants.X_25519_KEY_SIZE + dhOffset)];
                    ikmBuildSpan[..Constants.X_25519_KEY_SIZE].Fill(0xFF);
                    dhResultsSpan[..dhOffset].CopyTo(ikmBuildSpan[Constants.X_25519_KEY_SIZE..]);

                    Result<Unit, EcliptixProtocolFailure> hkdfResult =
                        DeriveHkdfKey(ikmBuildSpan, infoArray, hkdfOutputSpan);
                    if (hkdfResult.IsErr)
                    {
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(hkdfResult.UnwrapErr());
                    }

                    Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> handleResult =
                        AllocateAndWriteSecureHandle(hkdfOutputSpan);
                    if (handleResult.IsErr)
                    {
                        return handleResult;
                    }

                    secureOutputHandle = handleResult.Unwrap();
                    SodiumSecureMemoryHandle returnHandle = secureOutputHandle;
                    secureOutputHandle = null;
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(returnHandle);
                });
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(
                    EcliptixProtocolFailureMessages.IdentityKeys.UNEXPECTED_ERROR_DURING_X_3DH_DERIVATION,
                    ex));
        }
        finally
        {
            ephemeralHandleUsed?.Dispose();
            secureOutputHandle?.Dispose();
        }
    }

    public static Result<bool, EcliptixProtocolFailure> VerifyRemoteSpkSignature(
        ReadOnlySpan<byte> remoteIdentityEd25519, ReadOnlySpan<byte> remoteSpkPublic,
        ReadOnlySpan<byte> remoteSpkSignature)
    {
        if (remoteIdentityEd25519.Length != Constants.ED_25519_PUBLIC_KEY_SIZE ||
            remoteSpkPublic.Length != Constants.X_25519_PUBLIC_KEY_SIZE ||
            remoteSpkSignature.Length != Constants.ED_25519_SIGNATURE_SIZE)
        {
            return Result<bool, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.IdentityKeys
                    .INVALID_KEY_OR_SIGNATURE_LENGTH_FOR_SPK_VERIFICATION));
        }

        byte[]? signatureArray = null;
        byte[]? spkPublicArray = null;
        byte[]? identityArray = null;

        try
        {
            signatureArray = remoteSpkSignature.ToArray();
            spkPublicArray = remoteSpkPublic.ToArray();
            identityArray = remoteIdentityEd25519.ToArray();

            bool verificationResult = PublicKeyAuth.VerifyDetached(signatureArray, spkPublicArray, identityArray);

            return verificationResult
                ? Result<bool, EcliptixProtocolFailure>.Ok(true)
                : Result<bool, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Handshake(EcliptixProtocolFailureMessages.IdentityKeys
                        .REMOTE_SPK_SIGNATURE_VERIFICATION_FAILED));
        }
        finally
        {
            if (signatureArray != null)
            {
                SodiumInterop.SecureWipe(signatureArray);
            }

            if (spkPublicArray != null)
            {
                SodiumInterop.SecureWipe(spkPublicArray);
            }

            if (identityArray != null)
            {
                SodiumInterop.SecureWipe(identityArray);
            }
        }
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        return _disposed
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixSystemIdentityKeys)))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateHkdfInfo(ReadOnlySpan<byte> info)
    {
        return info.IsEmpty
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(
                    EcliptixProtocolFailureMessages.IdentityKeys.HKDF_INFO_CANNOT_BE_EMPTY))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateRemoteBundle(LocalPublicKeyBundle? remoteBundle)
    {
        if (remoteBundle == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.IdentityKeys
                    .REMOTE_BUNDLE_CANNOT_BE_NULL));
        }

        if (remoteBundle.IdentityX25519 is not { Length: Constants.X_25519_PUBLIC_KEY_SIZE })
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey(EcliptixProtocolFailureMessages.IdentityKeys
                    .INVALID_REMOTE_IDENTITY_X_25519_KEY));
        }

        if (remoteBundle.SignedPreKeyPublic is not { Length: Constants.X_25519_PUBLIC_KEY_SIZE })
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey(EcliptixProtocolFailureMessages.IdentityKeys
                    .INVALID_REMOTE_SIGNED_PRE_KEY_PUBLIC_KEY));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<Unit, EcliptixProtocolFailure> EnsureLocalKeysValid()
    {
        if (_ephemeralSecretKeyHandle == null || _ephemeralSecretKeyHandle.IsInvalid)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal(EcliptixProtocolFailureMessages.IdentityKeys
                    .LOCAL_EPHEMERAL_KEY_MISSING_OR_INVALID));
        }

        if (_identityX25519SecretKeyHandle.IsInvalid)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal(EcliptixProtocolFailureMessages.IdentityKeys
                    .LOCAL_IDENTITY_KEY_MISSING_OR_INVALID));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static void CleanupKeyHandlesAndOpks(
        SodiumSecureMemoryHandle? edSkHandle,
        SodiumSecureMemoryHandle? idXSkHandle,
        SodiumSecureMemoryHandle? spkSkHandle,
        List<OneTimePreKeyLocal>? opks)
    {
        edSkHandle?.Dispose();
        idXSkHandle?.Dispose();
        spkSkHandle?.Dispose();
        if (opks != null)
        {
            foreach (OneTimePreKeyLocal opk in opks)
            {
                opk.Dispose();
            }
        }
    }

    private static void SecureWipeDhResults(byte[]? dh1, byte[]? dh2, byte[]? dh3)
    {
        if (dh1 != null)
        {
            SodiumInterop.SecureWipe(dh1);
        }

        if (dh2 != null)
        {
            SodiumInterop.SecureWipe(dh2);
        }

        if (dh3 != null)
        {
            SodiumInterop.SecureWipe(dh3);
        }
    }

    private static Result<Unit, EcliptixProtocolFailure> DeriveHkdfKey(
        Span<byte> ikmBuildSpan,
        byte[] infoArray,
        Span<byte> hkdfOutputSpan)
    {
        byte[]? ikmArray = null;
        try
        {
            ikmArray = ikmBuildSpan.ToArray();
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: ikmArray,
                output: hkdfOutputSpan,
                salt: null,
                info: infoArray
            );
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            if (ikmArray != null)
            {
                SodiumInterop.SecureWipe(ikmArray);
            }
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> AllocateAndWriteSecureHandle(
        Span<byte> hkdfOutputSpan)
    {
        Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(Constants.X_25519_KEY_SIZE).MapSodiumFailure();
        if (allocResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());
        }

        SodiumSecureMemoryHandle secureOutputHandle = allocResult.Unwrap();
        Result<Unit, EcliptixProtocolFailure> writeResult =
            secureOutputHandle.Write(hkdfOutputSpan).MapSodiumFailure();
        if (writeResult.IsErr)
        {
            secureOutputHandle.Dispose();
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
        }

        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(secureOutputHandle);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            SecureCleanupLogic();
        }

        _disposed = true;
    }

    private void SecureCleanupLogic()
    {
        _ed25519SecretKeyHandle.Dispose();
        _identityX25519SecretKeyHandle.Dispose();
        _signedPreKeySecretKeyHandle.Dispose();
        _ephemeralSecretKeyHandle?.Dispose();
        foreach (OneTimePreKeyLocal opk in _oneTimePreKeysInternal)
        {
            opk.Dispose();
        }

        _oneTimePreKeysInternal.Clear();
        _oneTimePreKeysInternal = null!;

        _ephemeralSecretKeyHandle = null;
    }

    ~EcliptixSystemIdentityKeys()
    {
        Dispose(false);
    }
}
