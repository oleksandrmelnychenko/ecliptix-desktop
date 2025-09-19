namespace Ecliptix.Security.SSL.Native.Native;

public enum EcliptixResult
{
    Success = 0,
    ErrorVerificationFailed = -12,
}

public static class EcliptixConstants
{
    public const int Ed25519SignatureSize = 64;
}