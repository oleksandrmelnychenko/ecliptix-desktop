using System;
using System.Reactive;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels.Utilities;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class SignUpHostViewModel : ReactiveObject
{
    private UserControl? _currentView;

    public SignUpHostViewModel(MembershipViewFactory membershipViewFactory)
    {
        ShowView = ReactiveCommand.Create<MembershipViewType>(type =>
        {
            CurrentView = membershipViewFactory.Create(type);
        });

        ShowView.Execute(MembershipViewType.SignUpVerifyMobile)
            .Subscribe();
    }

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public ReactiveCommand<MembershipViewType, Unit> ShowView { get; }
}