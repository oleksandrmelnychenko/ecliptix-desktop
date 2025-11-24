using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Memberships.Components.Platform.OSX;
using Ecliptix.Core.Views.Memberships.Components.Platform.Windows;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships.Components;

public partial class TitleBar : ReactiveUserControl<TitleBarViewModel>
{
    private readonly ContentControl? _rootControl;
    private CompositeDisposable _pointerSubscriptions = new();

    private const double DRAG_THRESHOLD = 3;


    public TitleBar()
    {
        InitializeComponent();
        _rootControl = this.FindControl<ContentControl>("PART_Root");

        InitializeLayout();

        if (_rootControl != null)
        {
            _rootControl.PointerPressed += OnRootPointerPressed;
        }

        Unloaded += (s, e) =>
        {
            if (_rootControl != null)
            {
                _rootControl.PointerPressed -= OnRootPointerPressed;
            }

            _pointerSubscriptions.Dispose();
        };
    }

   private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Window? window = Window;
        TitleBarViewModel? viewModel = ViewModel;

        if (window == null || window.WindowState == WindowState.FullScreen || viewModel == null)
        {
            return;
        }

        if (!viewModel.IsDraggingEnabled)
        {
            return;
        }

        _pointerSubscriptions.Dispose();
        _pointerSubscriptions = new CompositeDisposable();

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Point startPosition = e.GetPosition(this);

            viewModel.IsDragging = false;

            IDisposable? moveSubscription = null;

            moveSubscription = Observable.FromEventPattern<PointerEventArgs>(
                h => _rootControl!.PointerMoved += h,
                h => _rootControl!.PointerMoved -= h
            )
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(args =>
            {
                if (viewModel.IsDragging)
                {
                    return;
                }

                Point currentPosition = args.EventArgs.GetPosition(this);
                Vector delta = startPosition - currentPosition;

                if (Math.Abs(delta.X) > DRAG_THRESHOLD || Math.Abs(delta.Y) > DRAG_THRESHOLD)
                {
                    viewModel.IsDragging = true;
                    window.BeginMoveDrag(e);

                    moveSubscription?.Dispose();
                }
            })
            .DisposeWith(_pointerSubscriptions);

            Observable.FromEventPattern<PointerReleasedEventArgs>(
                h => window.PointerReleased += h,
                h => window.PointerReleased -= h
            )
            .Take(1)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(args =>
            {
                if (!viewModel.IsDragging && e.ClickCount == 2 && !viewModel.DisableMaximizeButton)
                {
                    HandleDoubleClickMaximize();
                }

                viewModel.IsDragging = false;

                _pointerSubscriptions.Dispose();
            })
            .DisposeWith(_pointerSubscriptions);
        }
    }

    private void InitializeLayout()
    {
        if (_rootControl == null)
        {
            return;
        }

        if (_rootControl.Content != null)
        {
            return;
        }

        UserControl layout;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            layout = new MacosTitleBarLayout();
        }
        else
        {
            layout = new WindowsTitleBarLayout();
        }

        layout.Bind(DataContextProperty, this.GetObservable(DataContextProperty));

        _rootControl.Content = layout;
    }

    private void HandleDoubleClickMaximize()
    {
        if (Window == null)
        {
            return;
        }

        bool isCurrentlyMaximized = Window.WindowState == WindowState.Maximized;

        Window.WindowState = isCurrentlyMaximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private Window? Window => VisualRoot as Window;

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

