namespace Ecliptix.Feature.Authentication.Services.Authentication.Constants;

public static class VerificationMessageKeys
{
    public const string RESEND_COOLDOWN = "resend_cooldown_active";
    public const string OTP_MAX_ATTEMPTS_REACHED = "max_otp_attempts_reached";
    public const string SECURITY_RATE_LIMIT_EXCEEDED = "security_rate_limit_exceeded";
    public const string DEVICE_RATE_LIMIT_EXCEEDED = "device_rate_limit_exceeded";
    public const string MOBILE_OTP_LIMIT_EXHAUSTED = "mobile_otp_limit_exhausted";
    public const string OTP_EXPIRED = "otp_expired";
    public const string VERIFICATION_FLOW_EXPIRED = "flow_expired";
}
