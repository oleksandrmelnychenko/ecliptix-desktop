using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Settings.ViewModels;

namespace Ecliptix.Core.Features.Settings.Views;

public partial class SettingsView : ReactiveUserControl<SettingsViewModel>
{
    public SettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
