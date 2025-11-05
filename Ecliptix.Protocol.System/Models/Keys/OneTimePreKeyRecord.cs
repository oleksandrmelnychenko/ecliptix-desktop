using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Models.Keys;

internal record OneTimePreKeyRecord(uint PreKeyId, byte[] PublicKey)
{
    public static Result<OneTimePreKeyRecord, EcliptixProtocolFailure> Create(uint preKeyId, byte[] publicKey)
    {
        if (publicKey.Length != Constants.ED_25519_KEY_SIZE)
        {
            return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode(
                    $"One-time prekey public key must be {Constants.ED_25519_KEY_SIZE} bytes."));
        }

        return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Ok(new OneTimePreKeyRecord(preKeyId, publicKey));
    }
}
