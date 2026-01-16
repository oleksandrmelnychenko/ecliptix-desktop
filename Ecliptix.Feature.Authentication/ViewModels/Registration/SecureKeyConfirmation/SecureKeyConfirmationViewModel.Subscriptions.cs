using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using ReactiveUI;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel
{
    private void SetupSubscriptions()
    {
        this.WhenActivated(disposables =>
        {
            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe(
                    result =>
                    {
                        IsMembershipLoading = false;

                        if (!result.IsErr)
                        {
                            return;
                        }

                        ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                        ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                    },
                    _ =>
                    {
                        IsMembershipLoading = false;
                        ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                        ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                    })
                .DisposeWith(disposables);

            SubmitCommand
                .Where(_ => !IsBusy && CanSubmit)
                .Subscribe(_ =>
                {
                    ((AuthenticationViewModel)HostScreen).ClearNavigationStack(true);
                    ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.PIN_SET_VIEW).Subscribe();
                })
                .DisposeWith(disposables);
        });
    }
}
