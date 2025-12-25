using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Suggestions.Views;

public partial class SuggestionsView : UserControl
{
    public SuggestionsView() => InitializeComponent();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
