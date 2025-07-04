namespace Ecliptix.Opaque.Protocol;

public enum OpaqueCryptoFailureType
{
    HashingValidPointFailed,
    DecryptFailure,
    EncryptFailure,
    InvalidInput,
    InvalidKeySignature,
    MacVerificationFailed
}