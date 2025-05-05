using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.ViewModels;

public class AuthorizationViewModel: PageViewModel
{
    private PageViewModel _currentView;
    public PageViewModel CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }
    
    public IRelayCommand OpenSignInCommand { get; }
    public IRelayCommand OpenSignUpCommand { get; }
    public IRelayCommand OpenForgotPasswordCommand { get; }
    public IRelayCommand OpenMainWindowCommand { get; }

    public AuthorizationViewModel(INavigationWindowService navigationService, PageFactory pageFactory, PageViewModel currentView)
        : base(navigationService, pageFactory)
    {
        _currentView = currentView;
        // Set default view e.g. SignIn
        CurrentView = pageFactory.GetPageViewModel(ApplicationPageNames.REGISTRATION);
        OpenSignInCommand = new RelayCommand(() =>
        {
            CurrentView = pageFactory.GetPageViewModel(ApplicationPageNames.LOGIN);
        });
        OpenSignUpCommand = new RelayCommand(() =>
        {
            CurrentView = pageFactory.GetPageViewModel(ApplicationPageNames.REGISTRATION);
        });
        OpenForgotPasswordCommand = new RelayCommand(() =>
        {
            CurrentView = pageFactory.GetPageViewModel(ApplicationPageNames.FORGOT_PASSWORD);
        });
        OpenMainWindowCommand = new RelayCommand(() =>
        {
            var mainVm = pageFactory.GetPageViewModel(ApplicationPageNames.MAIN);
            navigationService.NavigateToNewWindow(mainVm);
        });
       
    }
}