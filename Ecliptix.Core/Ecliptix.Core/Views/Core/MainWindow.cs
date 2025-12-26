using System;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Core.Views.Core.Constants;
using Ecliptix.Protobuf.Device;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Views.Core;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private volatile bool _isSaveInProgress;
    private bool _isDisposed;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);

        SetupLazyViewModelDependentLogic();

        Closing += OnWindowClosing;

#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void SetupLazyViewModelDependentLogic()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .OfType<MainWindowViewModel>()
                .Take(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(viewModel =>
                {
                    viewModel.GetPrimaryScreenWorkingArea = GetScreenWorkingAreaForWindow;
                    viewModel.OnWindowRepositionRequested += OnWindowRepositionRequested;

                    viewModel.SyncViewModelWithActualWindowSize = () =>
                    {
                        viewModel.WindowWidth = ClientSize.Width;
                        viewModel.WindowHeight = ClientSize.Height;
                    };

                    viewModel.CurrentPosition = Position;

                    Observable.FromEventPattern<EventHandler<PixelPointEventArgs>, PixelPointEventArgs>(
                            h => PositionChanged += h,
                            h => PositionChanged -= h)
                        .Select(e => e.EventArgs.Point)
                        .ObserveOn(RxApp.MainThreadScheduler)
                        .Subscribe(pos => viewModel.CurrentPosition = pos)
                        .DisposeWith(disposables);

                    LoadWindowPlacementAsync(viewModel).ContinueWith(t =>
                    {
                        if (t.IsFaulted && t.Exception != null)
                        {
                            Log.Error(t.Exception, "[MAIN-WINDOW] Cannot load window placement.");
                        }
                    }, TaskScheduler.Default);
                })
                .DisposeWith(disposables);

            SetupDynamicPlacementSaving(disposables);

            disposables.DisposeWith(_disposables);
        });
    }

    private void OnWindowRepositionRequested(PixelPoint position)
    {
        Position = position;
    }

    private void SetupDynamicPlacementSaving(CompositeDisposable disposables)
    {
        IObservable<bool> canSaveGate = this.GetObservable(WindowStateProperty)
            .StartWith(WindowState)
            .Select(state =>
            {
                if (state == WindowState.Normal)
                {
                    return Observable.Return(true).Delay(MainWindowConstants.TimeSpans.StateChangeDebounce);
                }

                return Observable.Return(false);
            })
            .Switch()
            .StartWith(WindowState == WindowState.Normal)
            .DistinctUntilChanged();

        IObservable<Unit> movesAndSizes = Observable.Merge(
            Observable.FromEventPattern<EventHandler<PixelPointEventArgs>, PixelPointEventArgs>(
                    h => PositionChanged += h,
                    h => PositionChanged -= h)
                .Select(_ => Unit.Default),
            this.GetObservable(ClientSizeProperty).Select(_ => Unit.Default)
        );

        movesAndSizes
            .WithLatestFrom(canSaveGate, (_, canSave) => canSave)
            .Where(canSave => canSave)
            .Throttle(MainWindowConstants.TimeSpans.PlacementSaveThrottle)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ =>
            {
                try
                {
                    if (ViewModel == null || WindowState != WindowState.Normal)
                    {
                        return;
                    }

                    await ViewModel.SavePlacementAsync(
                        WindowState,
                        Position,
                        ClientSize).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MAIN-WINDOW] Error in Rx-based dynamic save.");
                }
            })
            .DisposeWith(disposables);
    }

    private Rect GetScreenWorkingAreaForWindow()
    {
        try
        {
            Screen? screen = Screens.ScreenFromWindow(this);
            if (screen != null)
            {
                return screen.WorkingArea.ToRect(1.0);
            }

            Screen? primaryScreen = Screens.Primary;
            if (primaryScreen != null)
            {
                return primaryScreen.WorkingArea.ToRect(1.0);
            }

            return new Rect(0, 0,
                MainWindowConstants.Dimensions.FALLBACK_SCREEN_WIDTH,
                MainWindowConstants.Dimensions.FALLBACK_SCREEN_HEIGHT);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[MAIN-WINDOW] Error getting screen working area, using fallback.");
            return new Rect(0, 0,
                MainWindowConstants.Dimensions.FALLBACK_SCREEN_WIDTH,
                MainWindowConstants.Dimensions.FALLBACK_SCREEN_HEIGHT);
        }
    }

    private async Task LoadWindowPlacementAsync(MainWindowViewModel viewModel)
    {
        WindowPlacement? placement = await viewModel.LoadInitialPlacementAsync();
        if (placement == null || !placement.IsValidSave)
        {
            return;
        }

        PixelPoint savedPosition = new(placement.PositionX, placement.PositionY);

        bool isPositionVisible = Screens?.All?.Any(screen => screen.WorkingArea.Contains(savedPosition)) ?? false;

        if (isPositionVisible)
        {
            Position = savedPosition;
        }

        Dispatcher.UIThread.Invoke(() =>
        {
            Size clientSize = new(placement.ClientWidth, placement.ClientHeight);
            ClientSize = clientSize;
            viewModel.WindowWidth = clientSize.Width;
            viewModel.WindowHeight = clientSize.Height;

            WindowState windowState = (WindowState)placement.WindowState;
            WindowState = windowState;
            viewModel.WindowState = windowState;
        });
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isSaveInProgress)
        {
            return;
        }

        _isSaveInProgress = true;

        if (ViewModel == null || _isDisposed)
        {
            return;
        }

        e.Cancel = true;

        try
        {
            await ViewModel.SavePlacementAsync(
                WindowState,
                Position,
                ClientSize).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MAIN-WINDOW] Error saving placement on close.");
        }
        finally
        {
            if (!_isDisposed)
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => Close());
                }
                else
                {
                    Close();
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        Closing -= OnWindowClosing;

        if (ViewModel != null)
        {
            ViewModel.OnWindowRepositionRequested -= OnWindowRepositionRequested;
        }

        _disposables.Dispose();

        ViewModel?.Dispose();
    }
}
