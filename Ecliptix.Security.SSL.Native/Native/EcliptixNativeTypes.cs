namespace Ecliptix.Security.SSL.Native.Native;

public enum EcliptixResult
{
    Success = 0,
    ErrorInvalidParams = -1,
    ErrorCryptoFailure = -2,
    ErrorVerificationFailed = -3,
    ErrorInitFailed = -4
}