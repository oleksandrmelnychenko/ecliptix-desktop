using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Sodium;

namespace Ecliptix.Protocol.System.Models.Keys;

public readonly struct OneTimePreKeyLocal : IDisposable, IEquatable<OneTimePreKeyLocal>
{
    private readonly byte[] _publicKey;

    public uint PreKeyId { get; }
    internal SodiumSecureMemoryHandle PrivateKeyHandle { get; }

    public ReadOnlySpan<byte> PublicKeySpan => _publicKey;

    public byte[] GetPublicKeyCopy() => (byte[])_publicKey.Clone();

    private OneTimePreKeyLocal(uint preKeyId, SodiumSecureMemoryHandle privateKeyHandle, byte[] publicKey)
    {
        PreKeyId = preKeyId;
        PrivateKeyHandle = privateKeyHandle;
        _publicKey = publicKey;
    }

    public static OneTimePreKeyLocal CreateFromParts(uint preKeyId, SodiumSecureMemoryHandle privateKeyHandle,
        byte[] publicKey) =>
        new(preKeyId, privateKeyHandle, publicKey);

    public static Result<OneTimePreKeyLocal, EcliptixProtocolFailure> Generate(uint preKeyId)
    {
        SodiumSecureMemoryHandle? securePrivateKey = null;
        byte[]? tempPrivateKeyBytes = null;
        byte[]? tempPrivKeyCopy = null;

        try
        {
            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(Constants.X_25519_PRIVATE_KEY_SIZE)
                    .MapSodiumFailure();

            if (allocResult.IsErr)
            {
                return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr());
            }

            securePrivateKey = allocResult.Unwrap();

            tempPrivateKeyBytes = SodiumCore.GetRandomBytes(Constants.X_25519_PRIVATE_KEY_SIZE);

            Result<Unit, EcliptixProtocolFailure> writeResult =
                securePrivateKey.Write(tempPrivateKeyBytes).MapSodiumFailure();
            if (writeResult.IsErr)
            {
                securePrivateKey.Dispose();
                SodiumInterop.SecureWipe(tempPrivateKeyBytes);
                return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
            }

            SodiumInterop.SecureWipe(tempPrivateKeyBytes);
            tempPrivateKeyBytes = null;

            tempPrivKeyCopy = new byte[Constants.X_25519_PRIVATE_KEY_SIZE];
            Result<Unit, EcliptixProtocolFailure> readResult = securePrivateKey.Read(tempPrivKeyCopy)
                .MapSodiumFailure();
            if (readResult.IsErr)
            {
                securePrivateKey.Dispose();
                SodiumInterop.SecureWipe(tempPrivKeyCopy);
                return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(readResult.UnwrapErr());
            }

            Result<byte[], EcliptixProtocolFailure> deriveResult = Result<byte[], EcliptixProtocolFailure>.Try(
                () => ScalarMult.Base(tempPrivKeyCopy),
                ex =>
                    EcliptixProtocolFailure.DeriveKey(
                        $"Failed to derive public key for OPK ID {preKeyId} using ScalarMult.Base.",
                        ex)
            );

            SodiumInterop.SecureWipe(tempPrivKeyCopy);
            tempPrivKeyCopy = null;

            if (deriveResult.IsErr)
            {
                securePrivateKey.Dispose();
                return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(deriveResult.UnwrapErr());
            }

            byte[] publicKeyBytes = deriveResult.Unwrap();

            if (publicKeyBytes.Length != Constants.X_25519_PUBLIC_KEY_SIZE)
            {
                securePrivateKey.Dispose();
                SodiumInterop.SecureWipe(publicKeyBytes);
                return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.DeriveKey(
                    $"Derived public key for OPK ID {preKeyId} has incorrect size ({publicKeyBytes.Length})."));
            }

            OneTimePreKeyLocal opk = new(preKeyId, securePrivateKey, publicKeyBytes);
            securePrivateKey = null;
            return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Ok(opk);
        }
        catch (Exception ex)
        {
            securePrivateKey?.Dispose();
            if (tempPrivateKeyBytes != null)
            {
                SodiumInterop.SecureWipe(tempPrivateKeyBytes);
            }

            if (tempPrivKeyCopy != null)
            {
                SodiumInterop.SecureWipe(tempPrivKeyCopy);
            }

            return Result<OneTimePreKeyLocal, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic($"Unexpected failure during OPK Generation for ID {preKeyId}.", ex)
            );
        }
    }

    public bool Equals(OneTimePreKeyLocal other) => PreKeyId == other.PreKeyId;

    public override bool Equals(object? obj) => obj is OneTimePreKeyLocal other && Equals(other);

    public override int GetHashCode() => PreKeyId.GetHashCode();

    public static bool operator ==(OneTimePreKeyLocal left, OneTimePreKeyLocal right) => left.Equals(right);

    public static bool operator !=(OneTimePreKeyLocal left, OneTimePreKeyLocal right) => !left.Equals(right);

    public void Dispose()
    {
        if (PrivateKeyHandle is { IsInvalid: false })
        {
            PrivateKeyHandle.Dispose();
        }
    }
}
