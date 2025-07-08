using System;
using Avalonia.Controls;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public  class RegistrationWizardViewModel : ViewModelBase
{
    private UserControl? _currentView;

    public RegistrationWizardViewModel( )
    {
        /*MessageBus.Current.Listen<VerifyCodeNavigateToView>("VerifyCodeNavigateToView")
            .Subscribe(c =>
            {
                
                CurrentView = authenticationViewFactory.Create(c.ViewTypeToNav);
                MessageBus.Current.SendMessage(c.Mobile, "Mobile");
            });

        ShowView = ReactiveCommand.Create<AuthViewType>(type =>
        {
            CurrentView = authenticationViewFactory.Create(type);
        });

        ShowView.Execute(AuthViewType.PhoneVerification)
            .Subscribe();*/
    }

    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public ReactiveCommand<AuthViewType, Unit> ShowView { get; }
}