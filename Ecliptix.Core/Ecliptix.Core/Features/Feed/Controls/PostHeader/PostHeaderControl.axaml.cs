using Avalonia.Controls;
using Ecliptix.Core.Features.Feed.ViewModels;

namespace Ecliptix.Core.Features.Feed.Controls.PostHeader;

public partial class PostHeaderControl : UserControl
{
    public PostHeaderControl() => InitializeComponent();

    private void Flyout_Opened(object? sender, System.EventArgs e)
    {
        if (DataContext is FeedItemViewModel viewModel)
        {
            viewModel.IsFlyoutOpen = true;
        }
    }

    private void Flyout_Closed(object? sender, System.EventArgs e)
    {
        if (DataContext is FeedItemViewModel viewModel)
        {
            viewModel.IsFlyoutOpen = false;
        }
    }
}
