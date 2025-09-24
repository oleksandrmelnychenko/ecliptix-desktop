using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Services;

public readonly struct CertificatePinningByteArrayResult
{
    private CertificatePinningByteArrayResult(bool isSuccess, byte[]? value, CertificatePinningFailure? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public byte[]? Value { get; }

    public CertificatePinningFailure? Error { get; }

    public static CertificatePinningByteArrayResult FromValue(byte[] value) => new(true, value, null);
    public static CertificatePinningByteArrayResult FromError(CertificatePinningFailure error) => new(false, null, error);
}