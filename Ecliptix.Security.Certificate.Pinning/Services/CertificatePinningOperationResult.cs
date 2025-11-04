using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Services;

public readonly struct CertificatePinningOperationResult
{
    private CertificatePinningOperationResult(bool isSuccess, CertificatePinningFailure? error)
    {
        IsSuccess = isSuccess;
        ERROR = error;
    }

    public bool IsSuccess { get; }

    public CertificatePinningFailure? ERROR { get; }

    public static CertificatePinningOperationResult Success() => new(true, null);
    public static CertificatePinningOperationResult FromError(CertificatePinningFailure error) => new(false, error);
}
