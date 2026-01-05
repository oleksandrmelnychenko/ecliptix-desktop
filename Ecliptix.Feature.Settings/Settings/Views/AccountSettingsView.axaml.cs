using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Settings.Settings.ViewModels;

namespace Ecliptix.Feature.Settings.Settings.Views;

public partial class AccountSettingsView : ReactiveUserControl<AccountSettingsViewModel>
{
    public AccountSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

