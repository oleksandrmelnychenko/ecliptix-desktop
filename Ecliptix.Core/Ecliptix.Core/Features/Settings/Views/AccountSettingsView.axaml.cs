using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Settings.ViewModels;

namespace Ecliptix.Core.Features.Settings.Views;

public partial class AccountSettingsView : ReactiveUserControl<AccountSettingsViewModel>
{
    public AccountSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

