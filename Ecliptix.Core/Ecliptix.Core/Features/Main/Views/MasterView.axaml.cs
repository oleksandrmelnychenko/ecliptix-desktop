using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Features.Main.ViewModels;
using System.Reactive;

namespace Ecliptix.Core.Features.Main.Views;

public partial class MasterView : UserControl
{
    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOverlayClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MasterViewModel viewModel)
        {
            viewModel.NavigationSidebar.CloseProfileMenuCommand.Execute(Unit.Default);
        }
    }
}
