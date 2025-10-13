namespace Ecliptix.Security.Certificate.Pinning.Native;

public enum CertificatePinningNativeResult
{
    Success = 0,
    ErrorInvalidParams = -1,
    ErrorCryptoFailure = -2,
    ErrorVerificationFailed = -3,
    ErrorInitFailed = -4
}
