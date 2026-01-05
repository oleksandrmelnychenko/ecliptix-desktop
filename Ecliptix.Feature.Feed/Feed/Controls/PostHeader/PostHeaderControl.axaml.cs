using Avalonia.Controls;
using Ecliptix.Feature.Feed.Feed.ViewModels;

namespace Ecliptix.Feature.Feed.Feed.Controls.PostHeader;

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
