using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Settings.Settings.ViewModels;

public class SecuritySettingsViewModel : ReactiveObject
{
    [Reactive] public string CurrentPassword { get; set; } = string.Empty;
    [Reactive] public string NewPassword { get; set; } = string.Empty;
    [Reactive] public string ConfirmPassword { get; set; } = string.Empty;

    [Reactive] public bool IsTwoFactorEnabled { get; set; }

    public ReactiveCommand<SystemU, SystemU> ChangePasswordCommand { get; }
    public ReactiveCommand<SystemU, SystemU> RevokeSessionsCommand { get; }

    public SecuritySettingsViewModel()
    {

        ChangePasswordCommand = ReactiveCommand.Create(() =>
        {
            Log.Information("Password change requested.");

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
        });

        RevokeSessionsCommand = ReactiveCommand.Create(() =>
        {
            Log.Information("Revoke all active sessions requested.");
        });
    }
}
