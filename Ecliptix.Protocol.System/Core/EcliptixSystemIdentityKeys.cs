using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

public sealed class EcliptixSystemIdentityKeys : IDisposable
{
    private readonly byte[] _ed25519PublicKey;
    private readonly SodiumSecureMemoryHandle _ed25519SecretKeyHandle;
    private readonly SodiumSecureMemoryHandle _identityX25519SecretKeyHandle;
    private readonly uint _signedPreKeyId;
    private readonly byte[] _signedPreKeyPublic;
    private readonly SodiumSecureMemoryHandle _signedPreKeySecretKeyHandle;
    private readonly byte[] _signedPreKeySignature;
    private bool _disposed;
    private SodiumSecureMemoryHandle? _ephemeralSecretKeyHandle;
    private byte[]? _ephemeralX25519PublicKey;
    private List<OneTimePreKeyLocal> _oneTimePreKeysInternal;

    private EcliptixSystemIdentityKeys(
        SodiumSecureMemoryHandle edSk, byte[] edPk,
        SodiumSecureMemoryHandle idSk, byte[] idPk,
        uint spkId, SodiumSecureMemoryHandle spkSk, byte[] spkPk, byte[] spkSig,
        List<OneTimePreKeyLocal> opks)
    {
        _ed25519SecretKeyHandle = edSk;
        _ed25519PublicKey = edPk;
        _identityX25519SecretKeyHandle = idSk;
        IdentityX25519PublicKey = idPk;
        _signedPreKeyId = spkId;
        _signedPreKeySecretKeyHandle = spkSk;
        _signedPreKeyPublic = spkPk;
        _signedPreKeySignature = spkSig;
        _oneTimePreKeysInternal = opks;
        _disposed = false;
    }

    public byte[] IdentityX25519PublicKey { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Result<IdentityKeysState, EcliptixProtocolFailure> ToProtoState()
    {
        if (_disposed)
            return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixSystemIdentityKeys)));

        try
        {
            byte[] edSk = _ed25519SecretKeyHandle.ReadBytes(Constants.Ed25519SecretKeySize).Unwrap();
            byte[] idSk = _identityX25519SecretKeyHandle.ReadBytes(Constants.X25519PrivateKeySize).Unwrap();
            byte[] spkSk = _signedPreKeySecretKeyHandle.ReadBytes(Constants.X25519PrivateKeySize).Unwrap();

            List<OneTimePreKeySecret> opkProtos = [];
            opkProtos.AddRange(from opk in _oneTimePreKeysInternal
                let opkSkBytes = opk.PrivateKeyHandle.ReadBytes(Constants.X25519PrivateKeySize).Unwrap()
                select new OneTimePreKeySecret
                {
                    PreKeyId = opk.PreKeyId, PrivateKey = ByteString.CopyFrom(opkSkBytes),
                    PublicKey = ByteString.CopyFrom(opk.PublicKey)
                });

            IdentityKeysState proto = new()
            {
                Ed25519SecretKey = ByteString.CopyFrom(edSk),
                IdentityX25519SecretKey = ByteString.CopyFrom(idSk),
                SignedPreKeySecret = ByteString.CopyFrom(spkSk),
                Ed25519PublicKey = ByteString.CopyFrom(_ed25519PublicKey),
                IdentityX25519PublicKey = ByteString.CopyFrom(IdentityX25519PublicKey),
                SignedPreKeyId = _signedPreKeyId,
                SignedPreKeyPublic = ByteString.CopyFrom(_signedPreKeyPublic),
                SignedPreKeySignature = ByteString.CopyFrom(_signedPreKeySignature)
            };
            proto.OneTimePreKeys.AddRange(opkProtos);

            // Removed debug logging of all private keys and secrets for security

            return Result<IdentityKeysState, EcliptixProtocolFailure>.Ok(proto);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixSystemIdentityKeys] Error exporting to proto state: {ex.Message}");
            return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to export identity keys to proto state.", ex));
        }
    }

    public static Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> FromProtoState(IdentityKeysState proto)
    {
        SodiumSecureMemoryHandle? edSkHandle = null;
        SodiumSecureMemoryHandle? idXSkHandle = null;
        SodiumSecureMemoryHandle? spkSkHandle = null;
        List<OneTimePreKeyLocal>? opks = [];

        try
        {
            byte[]? edSkBytes = proto.Ed25519SecretKey.ToByteArray();
            edSkHandle = SodiumSecureMemoryHandle.Allocate(edSkBytes.Length).Unwrap();
            edSkHandle.Write(edSkBytes).Unwrap();

            byte[]? idSkBytes = proto.IdentityX25519SecretKey.ToByteArray();
            idXSkHandle = SodiumSecureMemoryHandle.Allocate(idSkBytes.Length).Unwrap();
            idXSkHandle.Write(idSkBytes).Unwrap();

            byte[]? spkSkBytes = proto.SignedPreKeySecret.ToByteArray();
            spkSkHandle = SodiumSecureMemoryHandle.Allocate(spkSkBytes.Length).Unwrap();
            spkSkHandle.Write(spkSkBytes).Unwrap();

            byte[]? edPk = proto.Ed25519PublicKey.ToByteArray();
            byte[]? idXPk = proto.IdentityX25519PublicKey.ToByteArray();
            byte[]? spkPk = proto.SignedPreKeyPublic.ToByteArray();
            byte[]? spkSig = proto.SignedPreKeySignature.ToByteArray();

            foreach (OneTimePreKeySecret? opkProto in proto.OneTimePreKeys)
            {
                SodiumSecureMemoryHandle skHandle =
                    SodiumSecureMemoryHandle.Allocate(opkProto.PrivateKey.Length).Unwrap();
                skHandle.Write(opkProto.PrivateKey.ToByteArray()).Unwrap();

                OneTimePreKeyLocal opk = OneTimePreKeyLocal.CreateFromParts(opkProto.PreKeyId, skHandle,
                    opkProto.PublicKey.ToByteArray());
                opks.Add(opk);
            }

            // Removed debug logging of all restored private keys and secrets for security

            EcliptixSystemIdentityKeys keys = new(
                edSkHandle, edPk,
                idXSkHandle, idXPk,
                proto.SignedPreKeyId, spkSkHandle, spkPk, spkSig,
                opks);

            edSkHandle = null;
            idXSkHandle = null;
            spkSkHandle = null;
            opks = null;

            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(keys);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixSystemIdentityKeys] Error restoring from proto state: {ex.Message}");
            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            opks?.ForEach(k => k.Dispose());
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to rehydrate EcliptixSystemIdentityKeys from proto.", ex));
        }
    }

    public static Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> Create(uint oneTimeKeyCount)
    {
        if (oneTimeKeyCount > int.MaxValue)
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Requested one-time key count exceeds practical limits."));

        SodiumSecureMemoryHandle? edSkHandle = null;
        byte[]? edPk = null;
        SodiumSecureMemoryHandle? idXSkHandle = null;
        byte[]? idXPk = null;
        uint spkId = 0;
        SodiumSecureMemoryHandle? spkSkHandle = null;
        byte[]? spkPk = null;
        byte[]? spkSig = null;
        List<OneTimePreKeyLocal>? opks = null;

        try
        {
            Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> overallResult =
                GenerateEd25519Keys()
                    .Bind(edKeys =>
                    {
                        (edSkHandle, edPk) = edKeys;
                       
                        // Removed debug logging of private key generation for security
                        return GenerateX25519IdentityKeys();
                    })
                    .Bind(idKeys =>
                    {
                        (idXSkHandle, idXPk) = idKeys;
                       
                        // Removed debug logging of identity key generation for security
                        spkId = GenerateRandomUInt32();
                        return GenerateX25519SignedPreKey(spkId);
                    })
                    .Bind(spkKeys =>
                    {
                        (spkSkHandle, spkPk) = spkKeys;
                     
                        // Removed debug logging of signed prekey generation for security
                        return SignSignedPreKey(edSkHandle!, spkPk!);
                    })
                    .Bind(signature =>
                    {
                        spkSig = signature;
                        // Removed debug logging of signature generation for security
                        return GenerateOneTimePreKeys(oneTimeKeyCount);
                    })
                    .Bind(generatedOpks =>
                    {
                        opks = generatedOpks;
                        EcliptixSystemIdentityKeys material = new(edSkHandle!, edPk!, idXSkHandle!, idXPk!, spkId,
                            spkSkHandle!, spkPk!, spkSig!, opks);
                        edSkHandle = null;
                        idXSkHandle = null;
                        spkSkHandle = null;
                        opks = null;
                        return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(material);
                    });

            if (overallResult.IsErr)
            {
                edSkHandle?.Dispose();
                idXSkHandle?.Dispose();
                spkSkHandle?.Dispose();
                if (opks != null)
                {
                    foreach (OneTimePreKeyLocal opk in opks) opk.Dispose();
                }
            }

            return overallResult;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixSystemIdentityKeys] Error creating identity keys: {ex.Message}");
            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            if (opks != null)
            {
                foreach (OneTimePreKeyLocal opk in opks) opk.Dispose();
            }
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic($"Unexpected error initializing LocalKeyMaterial: {ex.Message}", ex));
        }
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> GenerateEd25519Keys()
    {
        SodiumSecureMemoryHandle? skHandle = null;
        byte[]? skBytes = null;
        try
        {
            return Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure>.Try(() =>
            {
                KeyPair edKeyPair = PublicKeyAuth.GenerateKeyPair();
                skBytes = edKeyPair.PrivateKey;
                byte[] pkBytes = edKeyPair.PublicKey;

                skHandle = SodiumSecureMemoryHandle.Allocate(Constants.Ed25519SecretKeySize).Unwrap();
                skHandle.Write(skBytes).Unwrap();

                return (skHandle, pkBytes);
            }, ex => EcliptixProtocolFailure.KeyGeneration("Failed to generate Ed25519 key pair.", ex));
        }
        finally
        {
            if (skBytes != null) SodiumInterop.SecureWipe(skBytes).IgnoreResult();
        }
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure>
        GenerateX25519IdentityKeys()
    {
        return SodiumInterop.GenerateX25519KeyPair("Identity");
    }

    private static Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure>
        GenerateX25519SignedPreKey(uint id)
    {
        return SodiumInterop.GenerateX25519KeyPair($"Signed PreKey (ID: {id})");
    }

    private static Result<byte[], EcliptixProtocolFailure> SignSignedPreKey(SodiumSecureMemoryHandle edSkHandle,
        byte[] spkPk)
    {
        byte[]? tempEdSignKeyCopy = null;
        try
        {
            Result<byte[], EcliptixProtocolFailure> readResult =
                edSkHandle.ReadBytes(Constants.Ed25519SecretKeySize).MapSodiumFailure();
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());
            tempEdSignKeyCopy = readResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> signResult = Result<byte[], EcliptixProtocolFailure>.Try(
                () => PublicKeyAuth.SignDetached(spkPk, tempEdSignKeyCopy),
                ex => EcliptixProtocolFailure.Generic("Failed to sign signed prekey public key.", ex));

            if (signResult.IsErr) return signResult;

            byte[] signature = signResult.Unwrap();
            if (signature.Length != Constants.Ed25519SignatureSize)
            {
                SodiumInterop.SecureWipe(signature).IgnoreResult();
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Generated SPK signature has incorrect size ({signature.Length})."));
            }

            return Result<byte[], EcliptixProtocolFailure>.Ok(signature);
        }
        finally
        {
            if (tempEdSignKeyCopy != null) SodiumInterop.SecureWipe(tempEdSignKeyCopy).IgnoreResult();
        }
    }

    private static Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure> GenerateOneTimePreKeys(uint count)
    {
        if (count == 0)
            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Ok([]);

        List<OneTimePreKeyLocal> opks = new((int)count);
        HashSet<uint> usedIds = new((int)count);
        uint idCounter = 2;

        try
        {
            for (int i = 0; i < count; i++)
            {
                uint id = idCounter++;
                while (usedIds.Contains(id)) id = GenerateRandomUInt32();
                usedIds.Add(id);

                Result<OneTimePreKeyLocal, EcliptixProtocolFailure> opkResult = OneTimePreKeyLocal.Generate(id);
                if (opkResult.IsErr)
                {
                    foreach (OneTimePreKeyLocal generatedOpk in opks) generatedOpk.Dispose();
                    return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Err(opkResult.UnwrapErr());
                }

                OneTimePreKeyLocal opk = opkResult.Unwrap();
               
                // Removed debug logging of one-time prekey generation for security
                opks.Add(opk);
            }

            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Ok(opks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixSystemIdentityKeys] Error generating one-time prekeys: {ex.Message}");
            foreach (OneTimePreKeyLocal generatedOpk in opks) generatedOpk.Dispose();
            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration("Unexpected error generating one-time prekeys.", ex));
        }
    }

    private static uint GenerateRandomUInt32()
    {
        byte[] buffer = SodiumCore.GetRandomBytes(sizeof(uint));
        return BitConverter.ToUInt32(buffer, 0);
    }

    public Result<PublicKeyBundle, EcliptixProtocolFailure> CreatePublicBundle()
    {
        if (_disposed)
            return Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixSystemIdentityKeys)));

        return Result<PublicKeyBundle, EcliptixProtocolFailure>.Try(() =>
        {
            List<OneTimePreKeyRecord> opkRecords = _oneTimePreKeysInternal
                .Select(opkLocal => new OneTimePreKeyRecord(opkLocal.PreKeyId, opkLocal.PublicKey)).ToList();

            return new PublicKeyBundle(
                _ed25519PublicKey,
                IdentityX25519PublicKey,
                _signedPreKeyId,
                _signedPreKeyPublic,
                _signedPreKeySignature,
                opkRecords,
                _ephemeralX25519PublicKey);
        }, ex => EcliptixProtocolFailure.Generic("Failed to create public key bundle.", ex));
    }

    public void GenerateEphemeralKeyPair()
    {
        if (_disposed)
        {
            Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixSystemIdentityKeys)));
            return;
        }

        _ephemeralSecretKeyHandle?.Dispose();
        if (_ephemeralX25519PublicKey != null) SodiumInterop.SecureWipe(_ephemeralX25519PublicKey).IgnoreResult();

        Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> generationResult =
            SodiumInterop.GenerateX25519KeyPair("Ephemeral");
        generationResult.Switch(
            keys =>
            {
                _ephemeralSecretKeyHandle = keys.skHandle;
                _ephemeralX25519PublicKey = keys.pk;
               
                // Removed debug logging of ephemeral key generation for security
            },
            _ =>
            {
                _ephemeralSecretKeyHandle = null;
                _ephemeralX25519PublicKey = null;
            });
    }

    public Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> X3dhDeriveSharedSecret(
        PublicKeyBundle remoteBundle, ReadOnlySpan<byte> info)
    {
        SodiumSecureMemoryHandle? ephemeralHandleUsed = null;
        SodiumSecureMemoryHandle? secureOutputHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> hkdfInfoValidationResult = ValidateHkdfInfo(info);
            if (hkdfInfoValidationResult.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                    hkdfInfoValidationResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> validationResult = CheckDisposed()
                .Bind(_ => ValidateRemoteBundle(remoteBundle))
                .Bind(_ => EnsureLocalKeysValid());

            if (validationResult.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(validationResult.UnwrapErr());

            // Convert info span to array for use in lambda
            var infoArray = info.ToArray();

            // Use secure memory for all DH operations
            return SecureMemoryUtils.WithSecureBuffers(
                new[] { Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize, Constants.X25519KeySize * 4, Constants.X25519KeySize * 5, Constants.X25519KeySize },
                buffers =>
                {
                    var ephemeralSecretSpan = buffers[0].GetSpan().Slice(0, Constants.X25519PrivateKeySize);
                    var identitySecretSpan = buffers[1].GetSpan().Slice(0, Constants.X25519PrivateKeySize);
                    var dhResultsSpan = buffers[2].GetSpan().Slice(0, Constants.X25519KeySize * 4);
                    var ikmSpan = buffers[3].GetSpan().Slice(0, Constants.X25519KeySize * 5);
                    var hkdfOutputSpan = buffers[4].GetSpan().Slice(0, Constants.X25519KeySize);

                    // Read ephemeral and identity keys
                    var readEphResult = _ephemeralSecretKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readEphResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readEphResult.UnwrapErr());
                    var ephemeralSecretBytes = readEphResult.Unwrap();
                    ephemeralSecretBytes.CopyTo(ephemeralSecretSpan);
                    SodiumInterop.SecureWipe(ephemeralSecretBytes).IgnoreResult();

                    var readIdResult = _identityX25519SecretKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readIdResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readIdResult.UnwrapErr());
                    var identitySecretBytes = readIdResult.Unwrap();
                    identitySecretBytes.CopyTo(identitySecretSpan);
                    SodiumInterop.SecureWipe(identitySecretBytes).IgnoreResult();

                    ephemeralHandleUsed = _ephemeralSecretKeyHandle;
                    _ephemeralSecretKeyHandle = null;

                    // Perform DH calculations
                    bool useOpk = remoteBundle.OneTimePreKeys.FirstOrDefault()?.PublicKey is { Length: Constants.X25519PublicKeySize };
                    var dh1 = ScalarMult.Mult(ephemeralSecretSpan.ToArray(), remoteBundle.IdentityX25519);
                    var dh2 = ScalarMult.Mult(ephemeralSecretSpan.ToArray(), remoteBundle.SignedPreKeyPublic);
                    var dh3 = ScalarMult.Mult(identitySecretSpan.ToArray(), remoteBundle.SignedPreKeyPublic);
                    
                    Console.WriteLine($"[DESKTOP] X3DH AS INITIATOR:");
                    Console.WriteLine($"  DH1 (Eph * RemoteId): {Convert.ToHexString(dh1)}");
                    Console.WriteLine($"  DH2 (Eph * RemoteSpk): {Convert.ToHexString(dh2)}");
                    Console.WriteLine($"  DH3 (Id * RemoteSpk): {Convert.ToHexString(dh3)}");
                    
                    int dhOffset = 0;
                    dh1.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;
                    dh2.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;
                    dh3.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;

                    if (useOpk)
                    {
                        var dh4 = ScalarMult.Mult(ephemeralSecretSpan.ToArray(), remoteBundle.OneTimePreKeys[0].PublicKey);
                        dh4.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        SodiumInterop.SecureWipe(dh4).IgnoreResult();
                        dhOffset += Constants.X25519KeySize;
                    }

                    // Secure cleanup of DH results
                    SodiumInterop.SecureWipe(dh1).IgnoreResult();
                    SodiumInterop.SecureWipe(dh2).IgnoreResult();
                    SodiumInterop.SecureWipe(dh3).IgnoreResult();

                    // Build IKM: F || DH1 || DH2 || DH3 || [DH4]
                    var ikmBuildSpan = ikmSpan[..(Constants.X25519KeySize + dhOffset)];
                    ikmBuildSpan[..Constants.X25519KeySize].Fill(0xFF);
                    dhResultsSpan[..dhOffset].CopyTo(ikmBuildSpan[Constants.X25519KeySize..]);

                    // Derive shared secret with HKDF
                    using (var hkdf = new HkdfSha256(ikmBuildSpan.ToArray(), salt: null))
                    {
                        hkdf.Expand(infoArray, hkdfOutputSpan);
                    }

                    // Store result in secure handle
                    var allocResult = SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure();
                    if (allocResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());

                    secureOutputHandle = allocResult.Unwrap();
                    var writeResult = secureOutputHandle.Write(hkdfOutputSpan).MapSodiumFailure();
                    if (writeResult.IsErr)
                    {
                        secureOutputHandle.Dispose();
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
                    }

                    var returnHandle = secureOutputHandle;
                    secureOutputHandle = null;
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(returnHandle);
                });
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey("An unexpected error occurred during X3DH shared secret derivation.", ex));
        }
        finally
        {
            ephemeralHandleUsed?.Dispose();
            secureOutputHandle?.Dispose();
        }
    }

    public Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> CalculateSharedSecretAsRecipient(
        ReadOnlySpan<byte> remoteIdentityPublicKeyX, ReadOnlySpan<byte> remoteEphemeralPublicKeyX,
        uint? usedLocalOpkId, ReadOnlySpan<byte> info)
    {
        SodiumSecureMemoryHandle? secureOutputHandle = null;
        SodiumSecureMemoryHandle? opkSecretHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> hkdfInfoValidationResult = ValidateHkdfInfo(info);
            if (hkdfInfoValidationResult.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                    hkdfInfoValidationResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> remoteRecipientKeysValidationResult =
                ValidateRemoteRecipientKeys(remoteIdentityPublicKeyX, remoteEphemeralPublicKeyX);
            if (remoteRecipientKeysValidationResult.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                    remoteRecipientKeysValidationResult.UnwrapErr());

            if (usedLocalOpkId.HasValue)
            {
                Result<SodiumSecureMemoryHandle?, EcliptixProtocolFailure> findOpkResult =
                    FindLocalOpkHandle(usedLocalOpkId.Value);
                if (findOpkResult.IsErr)
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(findOpkResult.UnwrapErr());
                opkSecretHandle = findOpkResult.Unwrap();
            }

            // Convert spans to arrays for use in lambda
            var remoteIdentityArray = remoteIdentityPublicKeyX.ToArray();
            var remoteEphemeralArray = remoteEphemeralPublicKeyX.ToArray();
            var infoArray = info.ToArray();

            // Use secure memory for all DH operations
            int totalDhLength = Constants.X25519KeySize * 3 + (opkSecretHandle != null ? Constants.X25519KeySize : 0);
            return SecureMemoryUtils.WithSecureBuffers(
                new[] { Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize, 
                        Constants.X25519KeySize * 4, Constants.X25519KeySize + totalDhLength, Constants.X25519KeySize },
                buffers =>
                {
                    var identitySecretSpan = buffers[0].GetSpan().Slice(0, Constants.X25519PrivateKeySize);
                    var signedPreKeySecretSpan = buffers[1].GetSpan().Slice(0, Constants.X25519PrivateKeySize);
                    var oneTimePreKeySecretSpan = buffers[2].GetSpan().Slice(0, Constants.X25519PrivateKeySize);
                    var dhResultsSpan = buffers[3].GetSpan().Slice(0, Constants.X25519KeySize * 4);
                    var ikmSpan = buffers[4].GetSpan().Slice(0, Constants.X25519KeySize + totalDhLength);
                    var hkdfOutputSpan = buffers[5].GetSpan().Slice(0, Constants.X25519KeySize);

                    // Read identity and signed prekey secrets
                    var readIdResult = _identityX25519SecretKeyHandle.ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readIdResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readIdResult.UnwrapErr());
                    var identitySecretBytes = readIdResult.Unwrap();
                    identitySecretBytes.CopyTo(identitySecretSpan);
                    SodiumInterop.SecureWipe(identitySecretBytes).IgnoreResult();

                    var readSpkResult = _signedPreKeySecretKeyHandle.ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readSpkResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readSpkResult.UnwrapErr());
                    var signedPreKeySecretBytes = readSpkResult.Unwrap();
                    signedPreKeySecretBytes.CopyTo(signedPreKeySecretSpan);
                    SodiumInterop.SecureWipe(signedPreKeySecretBytes).IgnoreResult();

                    // Read one-time prekey if needed
                    bool useOpk = opkSecretHandle != null;
                    if (useOpk)
                    {
                        var readOpkResult = opkSecretHandle!.ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                        if (readOpkResult.IsErr)
                            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readOpkResult.UnwrapErr());
                        var oneTimePreKeySecretBytes = readOpkResult.Unwrap();
                        oneTimePreKeySecretBytes.CopyTo(oneTimePreKeySecretSpan);
                        SodiumInterop.SecureWipe(oneTimePreKeySecretBytes).IgnoreResult();
                    }

                    // Perform DH calculations
                    var dh1 = ScalarMult.Mult(identitySecretSpan.ToArray(), remoteEphemeralArray);
                    var dh2 = ScalarMult.Mult(signedPreKeySecretSpan.ToArray(), remoteEphemeralArray);
                    var dh3 = ScalarMult.Mult(signedPreKeySecretSpan.ToArray(), remoteIdentityArray);
                    
                    Console.WriteLine($"[DESKTOP] X3DH AS RECIPIENT:");
                    Console.WriteLine($"  DH1 (Id * RemoteEph): {Convert.ToHexString(dh1)}");
                    Console.WriteLine($"  DH2 (Spk * RemoteEph): {Convert.ToHexString(dh2)}");
                    Console.WriteLine($"  DH3 (Spk * RemoteId): {Convert.ToHexString(dh3)}");
                    
                    int dhOffset = 0;
                    dh1.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;
                    dh2.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;
                    dh3.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                    dhOffset += Constants.X25519KeySize;

                    if (useOpk)
                    {
                        var dh4 = ScalarMult.Mult(oneTimePreKeySecretSpan.ToArray(), remoteEphemeralArray);
                        dh4.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        SodiumInterop.SecureWipe(dh4).IgnoreResult();
                        dhOffset += Constants.X25519KeySize;
                    }

                    // Secure cleanup of DH results
                    SodiumInterop.SecureWipe(dh1).IgnoreResult();
                    SodiumInterop.SecureWipe(dh2).IgnoreResult();
                    SodiumInterop.SecureWipe(dh3).IgnoreResult();

                    // Build IKM: F || DH1 || DH2 || DH3 || [DH4]
                    var ikmBuildSpan = ikmSpan[..(Constants.X25519KeySize + dhOffset)];
                    ikmBuildSpan[..Constants.X25519KeySize].Fill(0xFF);
                    dhResultsSpan[..dhOffset].CopyTo(ikmBuildSpan[Constants.X25519KeySize..]);

                    // Derive shared secret with HKDF
                    using (var hkdf = new HkdfSha256(ikmBuildSpan.ToArray(), salt: null))
                    {
                        hkdf.Expand(infoArray, hkdfOutputSpan);
                    }

                    // Removed debug logging of shared secret for security

                    // Store result in secure handle
                    var allocResult = SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure();
                    if (allocResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());

                    secureOutputHandle = allocResult.Unwrap();
                    var writeResult = secureOutputHandle.Write(hkdfOutputSpan).MapSodiumFailure();
                    if (writeResult.IsErr)
                    {
                        secureOutputHandle.Dispose();
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
                    }

                    var returnHandle = secureOutputHandle;
                    secureOutputHandle = null;
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(returnHandle);
                });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EcliptixSystemIdentityKeys] Error deriving recipient shared secret: {ex.Message}");
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(
                    "An unexpected error occurred during Recipient shared secret derivation.", ex));
        }
        finally
        {
            secureOutputHandle?.Dispose();
        }
    }

    public static Result<bool, EcliptixProtocolFailure> VerifyRemoteSpkSignature(
        ReadOnlySpan<byte> remoteIdentityEd25519, ReadOnlySpan<byte> remoteSpkPublic,
        ReadOnlySpan<byte> remoteSpkSignature)
    {
        if (remoteIdentityEd25519.Length != Constants.Ed25519PublicKeySize ||
            remoteSpkPublic.Length != Constants.X25519PublicKeySize ||
            remoteSpkSignature.Length != Constants.Ed25519SignatureSize)
        {
            return Result<bool, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Invalid key or signature length for SPK verification."));
        }

        bool verificationResult = PublicKeyAuth.VerifyDetached(remoteSpkSignature.ToArray(), remoteSpkPublic.ToArray(),
            remoteIdentityEd25519.ToArray());

        return verificationResult
            ? Result<bool, EcliptixProtocolFailure>.Ok(true)
            : Result<bool, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Handshake("Remote SPK signature verification failed."));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        return _disposed
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixSystemIdentityKeys)))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateHkdfInfo(ReadOnlySpan<byte> info)
    {
        return info.IsEmpty
            ? Result<Unit, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.DeriveKey("HKDF info cannot be empty."))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateRemoteBundle(PublicKeyBundle? remoteBundle)
    {
        if (remoteBundle == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Remote bundle cannot be null."));

        if (remoteBundle.IdentityX25519 is not { Length: Constants.X25519PublicKeySize })
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey("Invalid remote IdentityX25519 key."));

        if (remoteBundle.SignedPreKeyPublic is not { Length: Constants.X25519PublicKeySize })
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey("Invalid remote SignedPreKeyPublic key."));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<Unit, EcliptixProtocolFailure> EnsureLocalKeysValid()
    {
        if (_ephemeralSecretKeyHandle == null || _ephemeralSecretKeyHandle.IsInvalid)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal("Local ephemeral key is missing or invalid."));

        if (_identityX25519SecretKeyHandle.IsInvalid)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal("Local identity key is missing or invalid."));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateRemoteRecipientKeys(
        ReadOnlySpan<byte> remoteIdentityPublicKeyX, ReadOnlySpan<byte> remoteEphemeralPublicKeyX)
    {
        if (remoteIdentityPublicKeyX.Length != Constants.X25519PublicKeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey("Invalid remote Identity key length."));

        if (remoteEphemeralPublicKeyX.Length != Constants.X25519PublicKeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PeerPubKey("Invalid remote Ephemeral key length."));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<SodiumSecureMemoryHandle?, EcliptixProtocolFailure> FindLocalOpkHandle(uint opkId)
    {
        foreach (OneTimePreKeyLocal opk in _oneTimePreKeysInternal.Where(opk => opk.PreKeyId == opkId))
        {
            if (opk.PrivateKeyHandle.IsInvalid)
                return Result<SodiumSecureMemoryHandle?, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.PrepareLocal($"Local OPK ID {opkId} found but its handle is invalid."));
            return Result<SodiumSecureMemoryHandle?, EcliptixProtocolFailure>.Ok(opk.PrivateKeyHandle);
        }

        return Result<SodiumSecureMemoryHandle?, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Handshake($"Local OPK ID {opkId} not found."));
    }

    private static void ConcatenateDhResults(Span<byte> destination, byte[] dh1, byte[] dh2, byte[] dh3, byte[]? dh4)
    {
        int offset = 0;
        dh1.AsSpan(0, Constants.X25519KeySize).CopyTo(destination.Slice(offset));
        offset += Constants.X25519KeySize;
        dh2.AsSpan(0, Constants.X25519KeySize).CopyTo(destination.Slice(offset));
        offset += Constants.X25519KeySize;
        dh3.AsSpan(0, Constants.X25519KeySize).CopyTo(destination.Slice(offset));
        if (dh4 != null)
        {
            offset += Constants.X25519KeySize;
            dh4.AsSpan(0, Constants.X25519KeySize).CopyTo(destination.Slice(offset));
        }
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing) SecureCleanupLogic();
            _disposed = true;
        }
    }

    private void SecureCleanupLogic()
    {
        _ed25519SecretKeyHandle?.Dispose();
        _identityX25519SecretKeyHandle?.Dispose();
        _signedPreKeySecretKeyHandle?.Dispose();
        _ephemeralSecretKeyHandle?.Dispose();
        foreach (OneTimePreKeyLocal opk in _oneTimePreKeysInternal) opk.Dispose();
        _oneTimePreKeysInternal.Clear();
        _oneTimePreKeysInternal = null!;

        _ephemeralSecretKeyHandle = null;
    }

    ~EcliptixSystemIdentityKeys()
    {
        Dispose(false);
    }
}