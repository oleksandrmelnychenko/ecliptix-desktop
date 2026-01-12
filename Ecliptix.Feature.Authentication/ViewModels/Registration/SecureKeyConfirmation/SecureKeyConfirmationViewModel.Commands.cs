using System;
using System.Reactive.Linq;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(
                x => x.IsBusy,
                x => x.IsInNetworkOutage,
                x => x.IsMembershipLoading,
                (isBusy, isInOutage, isMembershipLoading) =>
                {
                    bool canExecute = !isBusy && !isInOutage && !isMembershipLoading;
                    return canExecute;
                })
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) =>
            {
                bool finalResult = canExecute && isValid;
                return finalResult;
            });

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitAsync, canExecuteSubmit);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
        canExecuteSubmit.ToPropertyEx(this, x => x.CanSubmit);
    }
}
