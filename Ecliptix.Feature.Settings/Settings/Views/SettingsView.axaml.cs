using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Settings.Settings.ViewModels;

namespace Ecliptix.Feature.Settings.Settings.Views;

public partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
