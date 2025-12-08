using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Settings.ViewModels;

namespace Ecliptix.Core.Features.Settings.Views;

public partial class AppearanceSettingsView : ReactiveUserControl<AppearanceSettingsViewModel>
{
    public AppearanceSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

