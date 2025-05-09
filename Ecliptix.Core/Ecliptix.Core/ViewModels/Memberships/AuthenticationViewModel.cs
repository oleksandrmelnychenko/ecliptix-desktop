using System;
using System.Collections.Generic;
using System.Reactive;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels.Utilities;
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

    public AuthenticationViewModel(MembershipViewFactory membershipViewFactory)
    {
        ShowView = ReactiveCommand.Create<MembershipViewType>(type =>
        {
            CurrentView = membershipViewFactory.Create(type);
        });

        ShowView.Execute(MembershipViewType.SignIn).Subscribe();
    }
}