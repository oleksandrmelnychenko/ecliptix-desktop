namespace Ecliptix.Core.Models.Membership;

public enum LogoutReason
{
    UserInitiated,
    SESSION_EXPIRED,
    SessionTimeout,
    DeviceRemoved,
    SecurityViolation,
    AccountDeactivated,
    SecureKeyChanged,
    ForceLogout,
    SystemMaintenance
}
