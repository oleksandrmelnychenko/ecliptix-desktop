using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Suggestions.Suggestions.Views;

public partial class SuggestionsView : UserControl
{
    public SuggestionsView() => InitializeComponent();

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
