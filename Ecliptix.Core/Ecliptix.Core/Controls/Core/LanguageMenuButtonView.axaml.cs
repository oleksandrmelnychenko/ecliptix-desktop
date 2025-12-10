using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public partial class LanguageMenuButtonView : ReactiveUserControl<LanguageMenuButtonViewModel>
{
    public LanguageMenuButtonView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

