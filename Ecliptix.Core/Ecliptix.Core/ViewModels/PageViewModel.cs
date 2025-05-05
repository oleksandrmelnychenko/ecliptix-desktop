using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.ViewModels;

public partial class PageViewModel : ViewModelBase
{
    [ObservableProperty]
    private ApplicationPageNames _pageName;

    protected readonly INavigationWindowService? NavigationService;
    protected readonly PageFactory? PageFactory;
    
    
    public PageViewModel() { }
    
    public PageViewModel(INavigationWindowService navigationService, PageFactory pageFactory)
    {
        NavigationService = navigationService;
        PageFactory = pageFactory;
    }
    
    [RelayCommand]
    protected virtual void OpenMainWindow()
    {
        if (NavigationService == null || PageFactory == null) return;
        var mainViewModel = PageFactory.GetPageViewModel(ApplicationPageNames.MAIN);
        NavigationService.NavigateToNewWindow(mainViewModel);
    }

    [RelayCommand]
    protected virtual void OpenSignIn()
    {
        if (NavigationService == null || PageFactory == null) return;
        var loginViewModel = PageFactory.GetPageViewModel(ApplicationPageNames.LOGIN);
        NavigationService.NavigateToNewWindow(loginViewModel);
    }

    [RelayCommand]
    protected virtual void OpenForgotPassword()
    {
        if (NavigationService == null || PageFactory == null) return;
        var forgotPasswordViewModel = PageFactory.GetPageViewModel(ApplicationPageNames.FORGOT_PASSWORD);
        NavigationService.NavigateToNewWindow(forgotPasswordViewModel);
    }
    
    [RelayCommand]
    private void GoToRegistration()
    {
        if (NavigationService == null || PageFactory == null) return;
        var registrationPage = PageFactory.GetPageViewModel(ApplicationPageNames.REGISTRATION);
        NavigationService.NavigateToNewWindow(registrationPage);
    }
}