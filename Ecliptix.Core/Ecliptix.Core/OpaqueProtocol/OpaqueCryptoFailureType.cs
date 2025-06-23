namespace Ecliptix.Core.OpaqueProtocol;

public enum OpaqueCryptoFailureType
{
    HashingValidPointFailed,
    DecryptFailure,
    EncryptFailure,
    InvalidInput,
    HashingFailure,
    InvalidKeySignature,
    TokenExpired,
}