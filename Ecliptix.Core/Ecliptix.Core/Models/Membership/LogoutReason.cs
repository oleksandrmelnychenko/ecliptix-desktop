namespace Ecliptix.Core.Models.Membership;

public enum LogoutReason
{
    UserInitiated,
    SessionExpired,
    SessionTimeout,
    DeviceRemoved,
    SecurityViolation,
    AccountDeactivated,
    SecureKeyChanged,
    ForceLogout,
    SystemMaintenance
}
