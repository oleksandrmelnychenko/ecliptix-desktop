using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Serilog;
using Serilog.Events;
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
            List<OneTimePreKeySecret> opkProtos = [];
            foreach (OneTimePreKeyLocal opk in _oneTimePreKeysInternal)
            {
                Result<ByteString, SodiumFailure> privateKeyResult =
                    SecureByteStringInterop.CreateByteStringFromSecureMemory(opk.PrivateKeyHandle,
                        Constants.X25519PrivateKeySize);
                if (privateKeyResult.IsErr)
                    return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(
                            $"Failed to read OPK private key: {privateKeyResult.UnwrapErr().Message}"));

                opkProtos.Add(new OneTimePreKeySecret
                {
                    PreKeyId = opk.PreKeyId,
                    PrivateKey = privateKeyResult.Unwrap(),
                    PublicKey = SecureByteStringInterop.CreateByteStringFromSpan(opk.PublicKey)
                });
            }

            Result<ByteString, SodiumFailure> ed25519SecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_ed25519SecretKeyHandle,
                    Constants.Ed25519SecretKeySize);
            if (ed25519SecretResult.IsErr)
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Failed to read Ed25519 secret key: {ed25519SecretResult.UnwrapErr().Message}"));

            Result<ByteString, SodiumFailure> identityX25519SecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_identityX25519SecretKeyHandle,
                    Constants.X25519PrivateKeySize);
            if (identityX25519SecretResult.IsErr)
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Failed to read Identity X25519 secret key: {identityX25519SecretResult.UnwrapErr().Message}"));

            Result<ByteString, SodiumFailure> signedPreKeySecretResult =
                SecureByteStringInterop.CreateByteStringFromSecureMemory(_signedPreKeySecretKeyHandle,
                    Constants.X25519PrivateKeySize);
            if (signedPreKeySecretResult.IsErr)
                return Result<IdentityKeysState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(
                        $"Failed to read Signed PreKey secret: {signedPreKeySecretResult.UnwrapErr().Message}"));

            IdentityKeysState proto = new()
            {
                Ed25519SecretKey = ed25519SecretResult.Unwrap(),
                IdentityX25519SecretKey = identityX25519SecretResult.Unwrap(),
                SignedPreKeySecret = signedPreKeySecretResult.Unwrap(),
                Ed25519PublicKey = SecureByteStringInterop.CreateByteStringFromSpan(_ed25519PublicKey),
                IdentityX25519PublicKey = SecureByteStringInterop.CreateByteStringFromSpan(IdentityX25519PublicKey),
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
                EcliptixProtocolFailure.Generic("Failed to export identity keys to proto state.", ex));
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

            SecureByteStringInterop.SecureCopyWithCleanup(proto.Ed25519PublicKey, out byte[]? edPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityX25519PublicKey, out byte[]? idXPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeyPublic, out byte[]? spkPk);
            SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeySignature, out byte[]? spkSig);

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
            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            if (opks == null)
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Failed to rehydrate EcliptixSystemIdentityKeys from proto.", ex));
            foreach (OneTimePreKeyLocal k in opks)
            {
                k.Dispose();
            }

            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to rehydrate EcliptixSystemIdentityKeys from proto.", ex));
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
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Requested one-time key count exceeds practical limits."));

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
                edSkHandle.Dispose();
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());
            }

            (idXSkHandle, byte[] idXPk) = idKeysResult.Unwrap();

            uint spkId = GenerateRandomUInt32();
            Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> spkKeysResult =
                GenerateX25519SignedPreKey(spkId);
            if (spkKeysResult.IsErr)
            {
                edSkHandle.Dispose();
                idXSkHandle.Dispose();
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(spkKeysResult.UnwrapErr());
            }

            (spkSkHandle, byte[] spkPk) = spkKeysResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> signatureResult = SignSignedPreKey(edSkHandle!, spkPk!);
            if (signatureResult.IsErr)
            {
                edSkHandle.Dispose();
                idXSkHandle.Dispose();
                spkSkHandle.Dispose();
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());
            }

            byte[]? spkSig = signatureResult.Unwrap();

            Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure> opksResult =
                GenerateOneTimePreKeys(oneTimeKeyCount);
            if (opksResult.IsErr)
            {
                edSkHandle.Dispose();
                idXSkHandle.Dispose();
                spkSkHandle.Dispose();
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(opksResult.UnwrapErr());
            }

            opks = opksResult.Unwrap();

            EcliptixSystemIdentityKeys material = new(edSkHandle, edPk, idXSkHandle, idXPk, spkId,
                spkSkHandle, spkPk, spkSig, opks);
            edSkHandle = null;
            idXSkHandle = null;
            spkSkHandle = null;
            opks = null;
            Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> overallResult =
                Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Ok(material);

            if (!overallResult.IsErr) return overallResult;
            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            if (opks == null) return overallResult;
            foreach (OneTimePreKeyLocal opk in opks) opk.Dispose();

            return overallResult;
        }
        catch (Exception ex)
        {
            edSkHandle?.Dispose();
            idXSkHandle?.Dispose();
            spkSkHandle?.Dispose();
            if (opks == null)
                return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic($"Unexpected error initializing LocalKeyMaterial: {ex.Message}",
                        ex));
            foreach (OneTimePreKeyLocal opk in opks) opk.Dispose();
            return Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic($"Unexpected error initializing LocalKeyMaterial: {ex.Message}", ex));
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

                skHandle = SodiumSecureMemoryHandle.Allocate(Constants.Ed25519SecretKeySize).Unwrap();
                skHandle.Write(skBytes).Unwrap();

                return (skHandle, pkBytes);
            }, ex => EcliptixProtocolFailure.KeyGeneration("Failed to generate Ed25519 key pair.", ex));
        }
        finally
        {
            if (skBytes != null) SodiumInterop.SecureWipe(skBytes);
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
            if (signature.Length == Constants.Ed25519SignatureSize)
                return Result<byte[], EcliptixProtocolFailure>.Ok(signature);
            SodiumInterop.SecureWipe(signature);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    $"Generated SPK signature has incorrect size ({signature.Length})."));
        }
        finally
        {
            if (tempEdSignKeyCopy != null) SodiumInterop.SecureWipe(tempEdSignKeyCopy);
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

                opks.Add(opk);
            }

            return Result<List<OneTimePreKeyLocal>, EcliptixProtocolFailure>.Ok(opks);
        }
        catch (Exception ex)
        {
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

        try
        {
            List<OneTimePreKeyRecord> opkRecords = [];
            opkRecords.AddRange(_oneTimePreKeysInternal.Select(opkLocal =>
                new OneTimePreKeyRecord(opkLocal.PreKeyId, opkLocal.PublicKey)));

            PublicKeyBundle bundle = new(
                _ed25519PublicKey,
                IdentityX25519PublicKey,
                _signedPreKeyId,
                _signedPreKeyPublic,
                _signedPreKeySignature,
                opkRecords,
                _ephemeralX25519PublicKey);

            return Result<PublicKeyBundle, EcliptixProtocolFailure>.Ok(bundle);
        }
        catch (Exception ex)
        {
            return Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to create public key bundle.", ex));
        }
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
        if (_ephemeralX25519PublicKey != null) SodiumInterop.SecureWipe(_ephemeralX25519PublicKey);

        Result<(SodiumSecureMemoryHandle skHandle, byte[] pk), EcliptixProtocolFailure> generationResult =
            SodiumInterop.GenerateX25519KeyPair("Ephemeral");

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

            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> bundleValidation = ValidateRemoteBundle(remoteBundle);
            if (bundleValidation.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(bundleValidation.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> localKeysValidation = EnsureLocalKeysValid();
            if (localKeysValidation.IsErr)
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(localKeysValidation.UnwrapErr());

            byte[] infoArray = info.ToArray();

            return SecureMemoryUtils.WithSecureBuffers(
                [
                    Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize, Constants.X25519KeySize * 4,
                    Constants.X25519KeySize * 5, Constants.X25519KeySize
                ],
                buffers =>
                {
                    Span<byte> ephemeralSecretSpan = buffers[0].GetSpan()[..Constants.X25519PrivateKeySize];
                    Span<byte> identitySecretSpan = buffers[1].GetSpan()[..Constants.X25519PrivateKeySize];
                    Span<byte> dhResultsSpan = buffers[2].GetSpan()[..(Constants.X25519KeySize * 4)];
                    Span<byte> ikmSpan = buffers[3].GetSpan()[..(Constants.X25519KeySize * 5)];
                    Span<byte> hkdfOutputSpan = buffers[4].GetSpan()[..Constants.X25519KeySize];

                    Result<byte[], EcliptixProtocolFailure> readEphResult = _ephemeralSecretKeyHandle!
                        .ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readEphResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readEphResult.UnwrapErr());
                    byte[] ephemeralSecretBytes = readEphResult.Unwrap();
                    ephemeralSecretBytes.CopyTo(ephemeralSecretSpan);
                    SodiumInterop.SecureWipe(ephemeralSecretBytes);

                    Result<byte[], EcliptixProtocolFailure> readIdResult = _identityX25519SecretKeyHandle!
                        .ReadBytes(Constants.X25519PrivateKeySize).MapSodiumFailure();
                    if (readIdResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readIdResult.UnwrapErr());
                    byte[] identitySecretBytes = readIdResult.Unwrap();
                    identitySecretBytes.CopyTo(identitySecretSpan);
                    SodiumInterop.SecureWipe(identitySecretBytes);

                    ephemeralHandleUsed = _ephemeralSecretKeyHandle;
                    _ephemeralSecretKeyHandle = null;

                    bool useOpk = remoteBundle.OneTimePreKeys.FirstOrDefault()?.PublicKey is
                        { Length: Constants.X25519PublicKeySize };

                    byte[]? ephemeralSecretArray = null;
                    byte[]? identitySecretArray = null;
                    byte[]? dh1 = null;
                    byte[]? dh2 = null;
                    byte[]? dh3 = null;
                    int dhOffset = 0;

                    try
                    {
                        ephemeralSecretArray = ephemeralSecretSpan.ToArray();
                        identitySecretArray = identitySecretSpan.ToArray();

                        byte[] dhTemp1 =
                            ScalarMult.Mult(identitySecretArray,
                                remoteBundle.SignedPreKeyPublic); // IK_client * SPK_server  
                        byte[] dhTemp2 =
                            ScalarMult.Mult(ephemeralSecretArray, remoteBundle.IdentityX25519); // EK_client * IK_server
                        byte[] dhTemp3 =
                            ScalarMult.Mult(ephemeralSecretArray,
                                remoteBundle.SignedPreKeyPublic); // EK_client * SPK_server

                        dh1 = dhTemp1; // IK_client * SPK_server (matches server's SPK_server * IK_client)
                        dh2 = dhTemp2; // EK_client * IK_server (matches server's IK_server * EK_client)  
                        dh3 = dhTemp3; // EK_client * SPK_server (matches server's SPK_server * EK_client)

                        if (Log.IsEnabled(LogEventLevel.Debug))
                            Log.Debug("X3DH key derivation as initiator - DH1: {DH1}, DH2: {DH2}, DH3: {DH3}",
                                Convert.ToHexString(dh1)[..16],
                                Convert.ToHexString(dh2)[..16],
                                Convert.ToHexString(dh3)[..16]);

                        dh1.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;
                        dh2.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;
                        dh3.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;

                        if (useOpk)
                        {
                            byte[]? dh4 = null;
                            try
                            {
                                dh4 = ScalarMult.Mult(ephemeralSecretArray, remoteBundle.OneTimePreKeys[0].PublicKey);
                                if (Log.IsEnabled(LogEventLevel.Debug))
                                    Log.Debug("X3DH DH4 (Eph * RemoteOpk): {DH4}", Convert.ToHexString(dh4)[..16]);
                                dh4.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                                dhOffset += Constants.X25519KeySize;
                            }
                            finally
                            {
                                if (dh4 != null) SodiumInterop.SecureWipe(dh4);
                            }
                        }
                    }
                    finally
                    {
                        if (ephemeralSecretArray != null) SodiumInterop.SecureWipe(ephemeralSecretArray);
                        if (identitySecretArray != null) SodiumInterop.SecureWipe(identitySecretArray);
                        if (dh1 != null) SodiumInterop.SecureWipe(dh1);
                        if (dh2 != null) SodiumInterop.SecureWipe(dh2);
                        if (dh3 != null) SodiumInterop.SecureWipe(dh3);
                    }

                    Span<byte> ikmBuildSpan = ikmSpan[..(Constants.X25519KeySize + dhOffset)];
                    ikmBuildSpan[..Constants.X25519KeySize].Fill(0xFF);
                    dhResultsSpan[..dhOffset].CopyTo(ikmBuildSpan[Constants.X25519KeySize..]);

                    byte[]? ikmArray = null;
                    try
                    {
                        ikmArray = ikmBuildSpan.ToArray();
                        if (Log.IsEnabled(LogEventLevel.Debug))
                            Log.Debug("X3DH IKM (Initiator) - Length: {IKMLength}, Prefix: {IKMPrefix}",
                                ikmArray.Length, Convert.ToHexString(ikmArray)[..32]);

                        HKDF.DeriveKey(
                            HashAlgorithmName.SHA256,
                            ikm: ikmArray,
                            output: hkdfOutputSpan,
                            salt: null,
                            info: infoArray
                        );

                        if (Log.IsEnabled(LogEventLevel.Debug))
                            Log.Debug("X3DH shared secret derived: {SharedSecretPrefix}",
                                Convert.ToHexString(hkdfOutputSpan.ToArray())[..32]);
                    }
                    finally
                    {
                        if (ikmArray != null) SodiumInterop.SecureWipe(ikmArray);
                    }

                    Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocResult =
                        SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure();
                    if (allocResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());

                    secureOutputHandle = allocResult.Unwrap();
                    Result<Unit, EcliptixProtocolFailure> writeResult =
                        secureOutputHandle.Write(hkdfOutputSpan).MapSodiumFailure();
                    if (writeResult.IsErr)
                    {
                        secureOutputHandle.Dispose();
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
                    }

                    SodiumSecureMemoryHandle returnHandle = secureOutputHandle;
                    secureOutputHandle = null;
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(returnHandle);
                });
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey("An unexpected error occurred during X3DH shared secret derivation.",
                    ex));
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

            byte[] remoteIdentityArray = remoteIdentityPublicKeyX.ToArray();
            byte[] remoteEphemeralArray = remoteEphemeralPublicKeyX.ToArray();
            byte[] infoArray = info.ToArray();

            int totalDhLength = Constants.X25519KeySize * 3 + (opkSecretHandle != null ? Constants.X25519KeySize : 0);
            return SecureMemoryUtils.WithSecureBuffers(
                [
                    Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize, Constants.X25519PrivateKeySize,
                    Constants.X25519KeySize * 4, Constants.X25519KeySize + totalDhLength, Constants.X25519KeySize
                ],
                buffers =>
                {
                    Span<byte> identitySecretSpan = buffers[0].GetSpan()[..Constants.X25519PrivateKeySize];
                    Span<byte> signedPreKeySecretSpan = buffers[1].GetSpan()[..Constants.X25519PrivateKeySize];
                    Span<byte> oneTimePreKeySecretSpan = buffers[2].GetSpan()[..Constants.X25519PrivateKeySize];
                    Span<byte> dhResultsSpan = buffers[3].GetSpan()[..(Constants.X25519KeySize * 4)];
                    Span<byte> ikmSpan = buffers[4].GetSpan()[..(Constants.X25519KeySize + totalDhLength)];
                    Span<byte> hkdfOutputSpan = buffers[5].GetSpan()[..Constants.X25519KeySize];

                    Result<byte[], EcliptixProtocolFailure> readIdResult = _identityX25519SecretKeyHandle
                        .ReadBytes(Constants.X25519PrivateKeySize)
                        .MapSodiumFailure();
                    if (readIdResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readIdResult.UnwrapErr());
                    byte[] identitySecretBytes = readIdResult.Unwrap();
                    identitySecretBytes.CopyTo(identitySecretSpan);
                    SodiumInterop.SecureWipe(identitySecretBytes);

                    Result<byte[], EcliptixProtocolFailure> readSpkResult = _signedPreKeySecretKeyHandle
                        .ReadBytes(Constants.X25519PrivateKeySize)
                        .MapSodiumFailure();
                    if (readSpkResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(readSpkResult.UnwrapErr());
                    byte[] signedPreKeySecretBytes = readSpkResult.Unwrap();
                    signedPreKeySecretBytes.CopyTo(signedPreKeySecretSpan);
                    SodiumInterop.SecureWipe(signedPreKeySecretBytes);

                    bool useOpk = opkSecretHandle != null;
                    if (useOpk)
                    {
                        Result<byte[], EcliptixProtocolFailure> readOpkResult = opkSecretHandle!
                            .ReadBytes(Constants.X25519PrivateKeySize)
                            .MapSodiumFailure();
                        if (readOpkResult.IsErr)
                            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                                readOpkResult.UnwrapErr());
                        byte[] oneTimePreKeySecretBytes = readOpkResult.Unwrap();
                        oneTimePreKeySecretBytes.CopyTo(oneTimePreKeySecretSpan);
                        SodiumInterop.SecureWipe(oneTimePreKeySecretBytes);
                    }

                    byte[]? identitySecretArray = null;
                    byte[]? signedPreKeySecretArray = null;
                    byte[]? oneTimePreKeySecretArray = null;
                    byte[]? dh1 = null;
                    byte[]? dh2 = null;
                    byte[]? dh3 = null;
                    int dhOffset = 0;

                    try
                    {
                        identitySecretArray = identitySecretSpan.ToArray();
                        signedPreKeySecretArray = signedPreKeySecretSpan.ToArray();
                        if (useOpk) oneTimePreKeySecretArray = oneTimePreKeySecretSpan.ToArray();

                        dh1 = ScalarMult.Mult(identitySecretArray, remoteEphemeralArray);
                        dh2 = ScalarMult.Mult(signedPreKeySecretArray, remoteEphemeralArray);
                        dh3 = ScalarMult.Mult(signedPreKeySecretArray, remoteIdentityArray);
                        dh1.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;
                        dh2.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;
                        dh3.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                        dhOffset += Constants.X25519KeySize;

                        if (useOpk)
                        {
                            byte[]? dh4 = null;
                            try
                            {
                                dh4 = ScalarMult.Mult(oneTimePreKeySecretArray!, remoteEphemeralArray);
                                dh4.AsSpan().CopyTo(dhResultsSpan[dhOffset..(dhOffset + Constants.X25519KeySize)]);
                                dhOffset += Constants.X25519KeySize;
                            }
                            finally
                            {
                                if (dh4 != null) SodiumInterop.SecureWipe(dh4);
                            }
                        }
                    }
                    finally
                    {
                        if (identitySecretArray != null) SodiumInterop.SecureWipe(identitySecretArray);
                        if (signedPreKeySecretArray != null) SodiumInterop.SecureWipe(signedPreKeySecretArray);
                        if (oneTimePreKeySecretArray != null) SodiumInterop.SecureWipe(oneTimePreKeySecretArray);
                        if (dh1 != null) SodiumInterop.SecureWipe(dh1);
                        if (dh2 != null) SodiumInterop.SecureWipe(dh2);
                        if (dh3 != null) SodiumInterop.SecureWipe(dh3);
                    }

                    Span<byte> ikmBuildSpan = ikmSpan[..(Constants.X25519KeySize + dhOffset)];
                    ikmBuildSpan[..Constants.X25519KeySize].Fill(0xFF);
                    dhResultsSpan[..dhOffset].CopyTo(ikmBuildSpan[Constants.X25519KeySize..]);

                    byte[]? ikmArray = null;
                    try
                    {
                        ikmArray = ikmBuildSpan.ToArray();
                        if (Log.IsEnabled(LogEventLevel.Debug))
                            Log.Debug("X3DH IKM (Responder) - Length: {IKMLength}, Prefix: {IKMPrefix}",
                                ikmArray.Length, Convert.ToHexString(ikmArray)[..32]);

                        HKDF.DeriveKey(
                            global::System.Security.Cryptography.HashAlgorithmName.SHA256,
                            ikm: ikmArray,
                            output: hkdfOutputSpan,
                            salt: null,
                            info: infoArray
                        );

                        if (Log.IsEnabled(LogEventLevel.Debug))
                            Log.Debug("X3DH shared secret derived: {SharedSecretPrefix}",
                                Convert.ToHexString(hkdfOutputSpan.ToArray())[..32]);
                    }
                    finally
                    {
                        if (ikmArray != null) SodiumInterop.SecureWipe(ikmArray);
                    }

                    Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocResult =
                        SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure();
                    if (allocResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());

                    secureOutputHandle = allocResult.Unwrap();
                    Result<Unit, EcliptixProtocolFailure> writeResult =
                        secureOutputHandle.Write(hkdfOutputSpan).MapSodiumFailure();
                    if (writeResult.IsErr)
                    {
                        secureOutputHandle.Dispose();
                        return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
                    }

                    SodiumSecureMemoryHandle? returnHandle = secureOutputHandle;
                    secureOutputHandle = null;
                    return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(returnHandle);
                });
        }
        catch (Exception ex)
        {
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
                    EcliptixProtocolFailure.Handshake("Remote SPK signature verification failed."));
        }
        finally
        {
            if (signatureArray != null) SodiumInterop.SecureWipe(signatureArray);
            if (spkPublicArray != null) SodiumInterop.SecureWipe(spkPublicArray);
            if (identityArray != null) SodiumInterop.SecureWipe(identityArray);
        }
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

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) SecureCleanupLogic();
        _disposed = true;
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