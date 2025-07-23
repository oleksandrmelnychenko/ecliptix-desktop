using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;

namespace Ecliptix.Core.Views.Memberships;

public partial class MembershipHostWindow : ReactiveWindow<MembershipHostWindowModel>
{
    public MembershipHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);
        #if DEBUG
                this.AttachDevTools();
        #endif

        this.Loaded += OnWindowLoaded;
    }
    
    private void OnWindowLoaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var notificationContainer = this.FindControl<StackPanel>("NotificationContainer");
        if (notificationContainer != null && ViewModel != null)
        {
            ViewModel.InitializeNotificationManager(notificationContainer);
        }
        
        this.Loaded -= OnWindowLoaded;
    }
}