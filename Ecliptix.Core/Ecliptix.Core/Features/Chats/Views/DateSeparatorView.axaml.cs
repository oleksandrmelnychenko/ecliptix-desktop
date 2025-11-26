using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views;

public partial class DateSeparatorView : UserControl
{
    public DateSeparatorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

