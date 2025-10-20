using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.AutoUpdater.Views;

public partial class UpdateNotificationBanner : UserControl
{
    public UpdateNotificationBanner()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
