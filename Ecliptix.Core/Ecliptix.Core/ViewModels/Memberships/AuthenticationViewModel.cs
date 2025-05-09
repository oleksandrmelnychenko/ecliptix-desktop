using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.Network;
using Ecliptix.Core.ViewModels.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class AuthenticationViewModel : ReactiveObject
{
    private UserControl? _currentView;

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public IReadOnlyList<MembershipViewType> MenuItems { get; }
        = Enum.GetValues<MembershipViewType>();

    public ReactiveCommand<MembershipViewType, Unit> ShowView { get; }

    public AuthenticationViewModel(
        MembershipViewFactory membershipViewFactory,
        NetworkController networkController)
    {
        Task.Run(() => networkController.InitiateKeyExchangeAsync(PubKeyExchangeType.AppDeviceEphemeralConnect));
        
        
        
        
        ShowView = ReactiveCommand.Create<MembershipViewType>(type =>
        {
            CurrentView = membershipViewFactory.Create(type);
        });

        ShowView.Execute(MembershipViewType.SignIn).Subscribe();
    }
}