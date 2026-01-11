using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Controls.TitleBarUtilities.Views;

public partial class ToggleNavigationSideBarView : ReactiveUserControl<ToggleNavigationSideBarViewModel>
{
    public ToggleNavigationSideBarView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

