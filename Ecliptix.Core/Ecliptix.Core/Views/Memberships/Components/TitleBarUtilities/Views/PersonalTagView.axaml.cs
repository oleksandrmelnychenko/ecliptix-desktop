using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.ViewModels;

namespace Ecliptix.Core.Views.Memberships.Components.TitleBarUtilities.Views;

public partial class PersonalTagView : ReactiveUserControl<PersonalTagViewModel>
{
    public PersonalTagView()
    {
        InitializeComponent();
    }

    private void  InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void CopyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PersonalTagViewModel vm && !string.IsNullOrWhiteSpace(vm.Tag))
        {
            TopLevel? topLevel = TopLevel.GetTopLevel(this);

            if (topLevel?.Clipboard is IClipboard clipboard)
            {
                await clipboard.SetTextAsync(vm.Tag);
            }
        }
    }
}

