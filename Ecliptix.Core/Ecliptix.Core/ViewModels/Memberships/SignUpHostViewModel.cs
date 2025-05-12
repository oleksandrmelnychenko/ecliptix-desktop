using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.ViewModels.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protobuf.Verification;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignUpHostViewModel : ViewModelBase
{
    private UserControl? _currentView;

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public ReactiveCommand<MembershipViewType, Unit> ShowView { get; }

    public SignUpHostViewModel(MembershipViewFactory membershipViewFactory)
    {
        MessageBus.Current.Listen<VerifyCodeNavigateToView>("VerifyCodeNavigateToView")
            .Subscribe(c =>
            {
                CurrentView = membershipViewFactory.Create(MembershipViewType.VerifyCode);
                MessageBus.Current.SendMessage(c.Mobile,"Mobile");
            });

        ShowView = ReactiveCommand.Create<MembershipViewType>(type =>
        {
            CurrentView = membershipViewFactory.Create(type);
        });

        ShowView.Execute(MembershipViewType.SignUpVerifyMobile)
            .Subscribe();
    }
}