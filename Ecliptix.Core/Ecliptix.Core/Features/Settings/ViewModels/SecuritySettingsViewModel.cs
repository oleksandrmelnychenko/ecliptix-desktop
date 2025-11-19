using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public class SecuritySettingsViewModel : ReactiveObject
{
    [Reactive] public string CurrentPassword { get; set; }
    [Reactive] public string NewPassword { get; set; }
    [Reactive] public string ConfirmPassword { get; set; }

    [Reactive] public bool IsTwoFactorEnabled { get; set; }

    public ReactiveCommand<SystemU, SystemU> ChangePasswordCommand { get; }
    public ReactiveCommand<SystemU, SystemU> RevokeSessionsCommand { get; }

    public SecuritySettingsViewModel()
    {
        // Команда зміни паролю
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
