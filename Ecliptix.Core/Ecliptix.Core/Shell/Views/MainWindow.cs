using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Avalonia.Win32;
using Ecliptix.Core.Controls.TitleBarUtilities.Providers;
using Ecliptix.Core.Shell.ViewModels;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Core.Shell.Views;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>, IDisposable
{
    private NativeDragProvider? _dragProvider;

    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _dragProvider = new NativeDragProvider(this, isDragging =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ViewModel?.TitleBarViewModel != null)
                {
                    ViewModel.TitleBarViewModel.IsDragging = isDragging;
                }
            });
        });

        SetupLazyViewModelDependentLogic();

    }

    private void SetupLazyViewModelDependentLogic()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .WhereNotNull()
                .OfType<MainWindowViewModel>()
                .Take(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(viewModel =>
                {
                    // РЕАКТИВНЕ ЛОГУВАННЯ: спостерігаємо за зміною стану
                    viewModel.TitleBarViewModel.WhenAnyValue(x => x.IsDragging)
                        .DistinctUntilChanged()
                        .Subscribe(isDragging =>
                        {
                            Log.Information("[DRAG-NOTIFY] State changed: {Status}",
                                isDragging ? "DRAGGING" : "IDLE");
                        })
                        .DisposeWith(disposables);

                    // Передаємо методи для ViewModel
                    viewModel.GetPrimaryScreenWorkingArea = GetScreenWorkingAreaForWindow;
                    viewModel.OnWindowRepositionRequested += OnWindowRepositionRequested;

                    // Початкова синхронізація розмірів
                    viewModel.WindowWidth = ClientSize.Width;
                    viewModel.WindowHeight = ClientSize.Height;
                })
                .DisposeWith(disposables);

            disposables.DisposeWith(_disposables);
        });
    }

    private void OnWindowRepositionRequested(PixelPoint position)
    {
        Position = position;
    }

    private Rect GetScreenWorkingAreaForWindow()
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        return screen?.WorkingArea.ToRect(1.0) ?? new Rect(0, 0, 1920, 1080);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _dragProvider?.Dispose();
        base.OnClosing(e);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposables.Dispose();
        ViewModel?.Dispose();
    }
}
