namespace Ecliptix.Core.Models.Membership;

/// <summary>
/// Reason for user logout, synchronized with server-side enum
/// </summary>
public enum LogoutReason
{
    /// <summary>
    /// User explicitly initiated logout
    /// </summary>
    UserInitiated,

    /// <summary>
    /// Session expired due to inactivity
    /// </summary>
    SessionExpired,

    /// <summary>
    /// Session timed out
    /// </summary>
    SessionTimeout,

    /// <summary>
    /// Device was removed from account
    /// </summary>
    DeviceRemoved,

    /// <summary>
    /// Security violation detected
    /// </summary>
    SecurityViolation,

    /// <summary>
    /// Account was deactivated
    /// </summary>
    AccountDeactivated,

    /// <summary>
    /// Password was changed, requiring re-authentication
    /// </summary>
    PasswordChanged,

    /// <summary>
    /// Force logout initiated by admin or system
    /// </summary>
    ForceLogout,

    /// <summary>
    /// System maintenance requiring logout
    /// </summary>
    SystemMaintenance
}
