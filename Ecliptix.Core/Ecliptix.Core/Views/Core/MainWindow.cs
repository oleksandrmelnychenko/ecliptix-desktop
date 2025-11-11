using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Protobuf.Device;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Core.Views.Core;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private bool _languageSelectorLoaded;
    private readonly Border? _languageSelectorContainer;
    private bool _isSaveInProgress;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);

        _languageSelectorContainer = this.FindControl<Border>("LanguageSelectorContainer");

        SetupLazyViewModelDependentLogic();
        SetupLanguageSelectorVisibility();

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
                .Where(dc => dc != null)
                .Select(dc => dc!)
                .OfType<MainWindowViewModel>()
                .Take(1)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(viewModel =>
                {

                    viewModel.GetPrimaryScreenWorkingArea = GetScreenWorkingAreaForWindow;
                    viewModel.OnWindowRepositionRequested += position =>
                    {
                        Position = position;
                    };

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


                    if (!_languageSelectorLoaded)
                    {
                        LoadLanguageSelector(viewModel);
                    }

                    LoadWindowPlacementAsync(viewModel).ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Log.Error(t.Exception, "[MAIN-WINDOW] Cannot load window placement.");
                        }
                    });
                })
                .DisposeWith(disposables);

            SetupDynamicPlacementSaving(disposables);
        });


    }

    private void SetupDynamicPlacementSaving(CompositeDisposable disposables)
    {
        IObservable<bool> canSaveGate = this.GetObservable(WindowStateProperty)
            .StartWith(WindowState)
            .Select(state =>
            {
                if (state == WindowState.Normal)
                {
                    return Observable.Return(true).Delay(TimeSpan.FromMilliseconds(100));
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
            .WithLatestFrom(canSaveGate, (moveEvent, canSave) => canSave)
            .Where(canSave => canSave)
            .Throttle(TimeSpan.FromMilliseconds(2000))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async void (_) =>
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
                        ClientSize);
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

            Log.Warning("[MAIN-WINDOW] No screen information available, using default bounds");
            return new Rect(0, 0, 1920, 1080);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MAIN-WINDOW] Error getting screen working area");
            return new Rect(0, 0, 1920, 1080);
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
        else
        {
            Log.Warning("[MAIN-WINDOW] The saved position of the window is out of bounds of the window, centering.");
        }

        Size clientSize = new(placement.ClientWidth, placement.ClientHeight);
        ClientSize = clientSize;
        viewModel.WindowWidth = clientSize.Width;
        viewModel.WindowHeight = clientSize.Height;

        WindowState windowState = (WindowState)placement.WindowState;
        WindowState = windowState;
        viewModel.WindowState = windowState;
    }

    private void SetupLanguageSelectorVisibility()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .Where(dc => dc != null)
                .Select(dc => dc!)
                .OfType<MainWindowViewModel>()
                .SelectMany(vm => vm.WhenAnyValue(x => x.CanResize))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(canResize =>
                {
                    if (_languageSelectorContainer != null)
                    {
                        _languageSelectorContainer.IsVisible = !canResize;
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private void LoadLanguageSelector(MainWindowViewModel viewModel)
    {
        if (_languageSelectorLoaded || _languageSelectorContainer == null)
        {
            return;
        }

        LanguageSelectorView languageSelector = new()
        {
            DataContext = viewModel.LanguageSelector
        };

        _languageSelectorContainer.Child = languageSelector;
        _languageSelectorLoaded = true;
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isSaveInProgress)
        {
            return;
        }

        if (ViewModel == null)
        {
            return;
        }

        e.Cancel = true;

        _isSaveInProgress = true;

        try
        {
            await ViewModel.SavePlacementAsync(
                WindowState,
                Position,
                ClientSize);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MAIN-WINDOW] Cannot save state during closing operation.");
        }
        finally
        {
            Close();
        }
    }
}
