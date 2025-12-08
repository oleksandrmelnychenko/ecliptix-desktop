using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class LanguageSelectionView : ReactiveUserControl<LanguageSelectionViewModel>
{
    public LanguageSelectionView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

