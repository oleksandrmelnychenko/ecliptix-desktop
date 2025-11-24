using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.Views;

public partial class ToggleNavigationSideBar : ReactiveUserControl<ToggleNavigationSideBarViewModel>
{
    public ToggleNavigationSideBar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

