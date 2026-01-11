using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Controls.TitleBarUtilities.Views;

public partial class ToggleThemeView : ReactiveUserControl<ToggleThemeViewModel>
{
    public ToggleThemeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

