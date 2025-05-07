using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels.Utilities;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class AuthorizationViewModel : ReactiveObject
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

    public AuthorizationViewModel()
    {
        ShowView = ReactiveCommand.Create<MembershipViewType>(type =>
        {
            CurrentView = MembershipViewFactory.Create(type);
        });

        ShowView.Execute(MembershipViewType.SignIn).Subscribe();
    }
}