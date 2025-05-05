using Ecliptix.Core.ViewModels;

namespace Ecliptix.Core.Services;

public interface INavigationService
{
    void Navigate(PageViewModel viewModel);
}

public interface INavigationViewService : INavigationService
{
    void NavigateInMainWindow(PageViewModel viewModel);
}

public interface INavigationWindowService : INavigationService
{
    void NavigateToNewWindow(PageViewModel viewModel);
}