using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using ReactiveUI;
using Serilog;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    private void SetupSubscriptions()
    {
        Log.Debug("[SECURE-KEY-VM] SetupSubscriptions: Setting up WhenActivated");

        this.WhenActivated(disposables =>
        {
            Log.Information("[SECURE-KEY-VM] WhenActivated: ViewModel activated, starting LoadMembershipAsync");

            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe(
                    result =>
                    {
                        IsMembershipLoading = false;
                        Log.Information("[SECURE-KEY-VM] LoadMembershipAsync subscription: Result received, IsErr={IsErr}",
                            result.IsErr);

                        if (!result.IsErr)
                        {
                            Log.Information("[SECURE-KEY-VM] LoadMembershipAsync subscription: Success, staying on current view");
                            return;
                        }

                        Log.Warning("[SECURE-KEY-VM] LoadMembershipAsync subscription: Error occurred, redirecting to WELCOME_VIEW. Error={Error}",
                            result.UnwrapErr().Message);
                        ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                        ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                    },
                    ex =>
                    {
                        IsMembershipLoading = false;
                        Log.Error(ex, "[SECURE-KEY-VM] LoadMembershipAsync subscription: Exception caught, redirecting to WELCOME_VIEW");
                        ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                        ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                    })
                .DisposeWith(disposables);

            // TODO: Re-enable PIN_SET_VIEW navigation when PIN feature is implemented
            // SubmitCommand
            //     .Where(_ => !IsBusy && CanSubmit)
            //     .Subscribe(_ =>
            //     {
            //         ((AuthenticationViewModel)HostScreen).ClearNavigationStack(true);
            //         ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.PIN_SET_VIEW).Subscribe();
            //     })
            //     .DisposeWith(disposables);
        });
    }
}
