using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships.Components;

public partial class TitleBar : ReactiveUserControl<TitleBarViewModel>
{
    public TitleBar()
    {
        InitializeComponent();

        this.WhenActivated(SetupPointerHandler);
    }

    private void SetupPointerHandler(CompositeDisposable disposables)
    {
        ContentControl? rootControl = this.FindControl<ContentControl>("PART_Root");
        if (rootControl == null)
        {
            return;
        }

        Observable.FromEventPattern<PointerPressedEventArgs>(
                h => rootControl.PointerPressed += h,
                h => rootControl.PointerPressed -= h
            )
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                if (Window == null)
                {
                    return;
                }

                if (Window.WindowState == WindowState.FullScreen)
                {
                    return;
                }

                if (e.EventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    if (e.EventArgs.ClickCount == 2)
                    {
                        Window.WindowState = Window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                    }
                    else
                    {
                        Window.BeginMoveDrag(e.EventArgs);
                    }
                }
            })
            .DisposeWith(disposables);
    }

    private Window? Window => VisualRoot as Window;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

