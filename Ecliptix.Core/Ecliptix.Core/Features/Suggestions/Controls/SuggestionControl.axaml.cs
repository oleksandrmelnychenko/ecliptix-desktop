using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Suggestions.Controls;

public partial class SuggestionControl : UserControl
{
    public SuggestionControl() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
