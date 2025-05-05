using System;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Services;

public class NavigationWindowService : INavigationWindowService 
{
    private readonly IServiceProvider _serviceProvider;

    public NavigationWindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Navigate(PageViewModel viewModel)
    {
        // This method comes from INavigationService interface
        // Implement the same behavior as NavigateToNewWindow
        NavigateToNewWindow(viewModel);
    }

    public void NavigateToNewWindow(PageViewModel viewModel)
    {
        if (viewModel is MainWindowViewModel mainViewModel)
        {
            var mainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };
            mainWindow.Show();
        }
    }
}