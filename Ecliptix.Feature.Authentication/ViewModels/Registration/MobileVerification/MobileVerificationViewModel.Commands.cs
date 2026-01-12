using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel
{
    public async Task HandleEnterKeyPressAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (VerifyMobileNumberCommand != null && await VerifyMobileNumberCommand.CanExecute.FirstOrDefaultAsync())
        {
            VerifyMobileNumberCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canVerify = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid);

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canVerify);
        VerifyMobileNumberCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        OpenCountryPickerCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await GlobalModalService.ShowRightAsync(
                new CountryCodeViewModel(_messageBus, CountryIso, CountryPickerContext),
                showScrim: true,
                isDismissable: true
            );
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() =>
        {
            bool success = BrowserHelper.OpenUrl(_settings.PrivacyPolicyUrl);
            if (!success)
            {
                Log.Warning("Failed to open privacy policy URL: {Url}", _settings.PrivacyPolicyUrl);
            }
        });

        OpenTermsOfServiceCommand = ReactiveCommand.Create(() =>
        {
            bool success = BrowserHelper.OpenUrl(_settings.TermsOfServiceUrl);
            if (!success)
            {
                Log.Warning("Failed to open privacy policy URL: {Url}", _settings.TermsOfServiceUrl);
            }
        });

        _disposables.Add(VerifyMobileNumberCommand);
    }
}
