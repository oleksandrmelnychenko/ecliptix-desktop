using Ecliptix.Core.ViewModels;
using Ecliptix.Core.Views;
using ReactiveUI;

namespace Ecliptix.Core.Services;

public class NavigationViewService : INavigationViewService
{
    private readonly ViewLocator _viewLocator;
    private readonly MainWindow _mainWindow;

    public NavigationViewService(MainWindow mainWindow)
    {
        _viewLocator = new ViewLocator();
        _mainWindow = mainWindow;
    }

    public void Navigate(PageViewModel viewModel)
    {
        NavigateInMainWindow(viewModel);
    }

    public void NavigateInMainWindow(PageViewModel viewModel)
    {
        if (_mainWindow.DataContext is MainWindowViewModel mainVM)
        {
            mainVM.CurrentView = viewModel;
        }
    }
}

