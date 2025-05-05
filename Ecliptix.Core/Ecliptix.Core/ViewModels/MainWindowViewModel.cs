using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.ViewModels;

public partial class MainWindowViewModel : PageViewModel
{
    [ObservableProperty] 
    private PageViewModel? _currentView;
    
    public MainWindowViewModel(INavigationWindowService navigationService, PageFactory pageFactory)
        : base(navigationService, pageFactory)
    {
        PageName = ApplicationPageNames.MAIN;
        CurrentView = this; // Set initial view to self
    }
}