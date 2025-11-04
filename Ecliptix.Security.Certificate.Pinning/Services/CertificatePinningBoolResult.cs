using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Services;

public readonly struct CertificatePinningBoolResult
{
    private CertificatePinningBoolResult(bool isSuccess, bool value, CertificatePinningFailure? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        ERROR = error;
    }

    public bool IsSuccess { get; }

    public bool Value { get; }

    public CertificatePinningFailure? ERROR { get; }

    public static CertificatePinningBoolResult FromValue(bool value) => new(true, value, null);
    public static CertificatePinningBoolResult FromError(CertificatePinningFailure error) => new(false, false, error);
}
