using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Views.Core.Constants;
using ReactiveUI;

namespace Ecliptix.Core.Views.Core.Components.TitleBar;

public partial class TitleBar : ReactiveUserControl<TitleBarViewModel>, IDisposable
{
    private readonly ContentControl? _rootControl;
    private readonly CompositeDisposable _disposables = new();
    private CompositeDisposable _pointerSubscriptions = new();
    private IDisposable? _dataContextBinding;
    private bool _isDisposed;

    public TitleBar()
    {
        InitializeComponent();
        _rootControl = this.FindControl<ContentControl>("PART_Root");

        InitializeLayout();

        if (_rootControl != null)
        {
            _rootControl.PointerPressed += OnRootPointerPressed;
        }
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isDisposed || _rootControl == null)
        {
            return;
        }

        Window? window = VisualRoot as Window;
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
                h => _rootControl.PointerMoved += h,
                h => _rootControl.PointerMoved -= h
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

                if (Math.Abs(delta.X) > MainWindowConstants.Layout.DRAG_THRESHOLD ||
                    Math.Abs(delta.Y) > MainWindowConstants.Layout.DRAG_THRESHOLD)
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
                    HandleDoubleClickMaximize(window);
                }

                viewModel.IsDragging = false;

                _pointerSubscriptions.Dispose();
            })
            .DisposeWith(_pointerSubscriptions);
        }
    }

    private void InitializeLayout()
    {
        if (_rootControl == null || _rootControl.Content != null)
        {
            return;
        }

        UserControl layout = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? new Platform.OSX.MacosTitleBarLayout()
            : new Platform.Windows.WindowsTitleBarLayout();

        _dataContextBinding = layout.Bind(DataContextProperty, this.GetObservable(DataContextProperty));

        _rootControl.Content = layout;
    }

    private static void HandleDoubleClickMaximize(Window window)
    {
        bool isCurrentlyMaximized = window.WindowState == WindowState.Maximized;

        window.WindowState = isCurrentlyMaximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_rootControl != null)
        {
            _rootControl.PointerPressed -= OnRootPointerPressed;
        }

        _dataContextBinding?.Dispose();
        _pointerSubscriptions.Dispose();
        _disposables.Dispose();

        if (_rootControl?.Content is IDisposable disposableContent)
        {
            disposableContent.Dispose();
        }
    }
}
