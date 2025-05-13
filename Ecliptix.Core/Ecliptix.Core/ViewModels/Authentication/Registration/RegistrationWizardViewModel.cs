using System;
using Avalonia.Controls;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public  class RegistrationWizardViewModel : ViewModelBase
{
    private UserControl? _currentView;

    public RegistrationWizardViewModel(AuthenticationViewFactory authenticationViewFactory)
    {
        MessageBus.Current.Listen<VerifyCodeNavigateToView>("VerifyCodeNavigateToView")
            .Subscribe(c =>
            {
                CurrentView = authenticationViewFactory.Create(AuthViewType.VerificationCodeEntry);
                MessageBus.Current.SendMessage(c.Mobile, "Mobile");
            });

        ShowView = ReactiveCommand.Create<AuthViewType>(type =>
        {
            CurrentView = authenticationViewFactory.Create(type);
        });

        ShowView.Execute(AuthViewType.VerificationCodeEntry)
            .Subscribe();
    }

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public ReactiveCommand<AuthViewType, Unit> ShowView { get; }
}