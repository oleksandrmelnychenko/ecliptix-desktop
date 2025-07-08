using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI; 
using Ecliptix.Core.ViewModels.Authentication;
using System.Threading.Tasks;
using ReactiveUI;

namespace Ecliptix.Core.Views.Authentication;

public partial class AuthenticationWindow : ReactiveWindow<MembershipHostWindowModel>
{
    public AuthenticationWindow()
    {
        this.WhenActivated(disposables =>
        {
            FadeIn();
        });
    }

    private async void FadeIn()
    {
        Opacity = 0;

        const double duration = 500; 
        const int steps = 30;

        for (int i = 0; i <= steps; i++)
        {
            if (!IsVisible) break; 
            Opacity = i / (double)steps;
            await Task.Delay((int)(duration / steps));
        }

        // Ensure opacity is exactly 1 at the end
        Opacity = 1;
    }

    private void TitleBarArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Button) 
        {
            BeginMoveDrag(e);
        }
    }

    private void Resize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is WindowEdge edge)
        {
            BeginResizeDrag(edge, e);
        }
    }
}