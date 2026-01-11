using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Settings.ViewModels;

namespace Ecliptix.Feature.Settings.Views;

public partial class SecuritySettingsView : ReactiveUserControl<SecuritySettingsViewModel>
{
    public SecuritySettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

