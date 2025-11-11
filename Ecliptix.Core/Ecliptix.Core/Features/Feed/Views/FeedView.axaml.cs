using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Feed.Views;

public partial class FeedView : UserControl
{
    public FeedView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
