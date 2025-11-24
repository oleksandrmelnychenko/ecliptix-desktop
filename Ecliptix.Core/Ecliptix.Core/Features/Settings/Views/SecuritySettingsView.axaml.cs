using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Settings.ViewModels;

namespace Ecliptix.Core.Features.Settings.Views;

public partial class SecuritySettingsView : ReactiveUserControl<SecuritySettingsViewModel>
{
    public SecuritySettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

