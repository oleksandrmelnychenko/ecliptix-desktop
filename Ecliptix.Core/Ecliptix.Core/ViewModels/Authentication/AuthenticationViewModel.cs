using System;
using System.Collections.Generic;
using System.Reactive;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.Network;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class AuthenticationViewModel : ReactiveObject
{
    private UserControl? _currentView;

    public AuthenticationViewModel(
        AuthenticationViewFactory authenticationViewFactory,
        NetworkController networkController)
    {
        ShowView = ReactiveCommand.Create<AuthViewType>(type =>
        {
            CurrentView = authenticationViewFactory.Create(type);
        });

        ShowView.Execute(AuthViewType.RegistrationWizard).Subscribe();
    }

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public IReadOnlyList<AuthViewType> MenuItems { get; }
        = Enum.GetValues<AuthViewType>();

    public ReactiveCommand<AuthViewType, Unit> ShowView { get; }
}