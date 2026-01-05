using Avalonia.Controls;
using Ecliptix.Feature.Feed.Feed.ViewModels;

namespace Ecliptix.Feature.Feed.Feed.Controls.Comments;

public partial class CommentControl : UserControl
{
    public CommentControl() => InitializeComponent();

    private void Flyout_Opened(object? sender, System.EventArgs e)
    {
        if (DataContext is CommentViewModel viewModel)
        {
            viewModel.IsFlyoutOpen = true;
        }
    }

    private void Flyout_Closed(object? sender, System.EventArgs e)
    {
        if (DataContext is CommentViewModel viewModel)
        {
            viewModel.IsFlyoutOpen = false;
        }
    }
}
