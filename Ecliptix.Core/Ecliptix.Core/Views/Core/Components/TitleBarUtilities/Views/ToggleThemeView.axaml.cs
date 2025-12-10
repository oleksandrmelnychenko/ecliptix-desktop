using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Core.Components.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Views.Core.Components.TitleBarUtilities.Views;

public partial class ToggleThemeView : ReactiveUserControl<ToggleThemeViewModel>
{
    public ToggleThemeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

