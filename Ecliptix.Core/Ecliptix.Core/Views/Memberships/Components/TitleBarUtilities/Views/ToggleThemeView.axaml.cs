using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.Views;

public partial class ToggleThemeView : ReactiveUserControl<ToggleThemeViewModel>
{
    public ToggleThemeView()
    {
        InitializeComponent();
    }
}

