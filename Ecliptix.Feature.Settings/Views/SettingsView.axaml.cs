using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Settings.ViewModels;

namespace Ecliptix.Feature.Settings.Views;

public partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
