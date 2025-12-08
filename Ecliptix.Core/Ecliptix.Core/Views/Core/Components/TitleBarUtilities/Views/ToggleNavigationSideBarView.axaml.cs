using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.Views;

public partial class ToggleNavigationSideBarView : ReactiveUserControl<ToggleNavigationSideBarViewModel>
{
    public ToggleNavigationSideBarView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

