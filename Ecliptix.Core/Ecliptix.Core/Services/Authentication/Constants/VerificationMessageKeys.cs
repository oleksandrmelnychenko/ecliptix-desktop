namespace Ecliptix.Core.Services.Authentication.Constants;

public static class VerificationMessageKeys
{
    public const string ResendCooldown = "resend_cooldown_active";
    public const string OtpMaxAttemptsReached = "max_otp_attempts_reached";
    public const string SecurityRateLimitExceeded = "security_rate_limit_exceeded";
    public const string DeviceRateLimitExceeded = "device_rate_limit_exceeded";
    public const string MobileOtpLimitExhausted = "mobile_otp_limit_exhausted";
    public const string OtpExpired = "otp_expired";
    public const string VerificationFlowExpired = "flow_expired";
}
