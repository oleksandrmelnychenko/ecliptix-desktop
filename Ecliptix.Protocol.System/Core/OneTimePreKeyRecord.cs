using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

internal record OneTimePreKeyRecord(uint PreKeyId, byte[] PublicKey)
{
    public static Result<OneTimePreKeyRecord, EcliptixProtocolFailure> Create(uint preKeyId, byte[] publicKey)
    {
        if (publicKey.Length != Constants.Ed25519KeySize)
        {
            return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode(
                    $"One-time prekey public key must be {Constants.Ed25519KeySize} bytes."));
        }

        return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Ok(new OneTimePreKeyRecord(preKeyId, publicKey));
    }
}
