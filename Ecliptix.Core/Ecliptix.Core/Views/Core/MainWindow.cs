using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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

            IObservable<EventPattern<PixelPointEventArgs>> positionChanged = Observable.FromEventPattern<PixelPointEventArgs>(
                handler => PositionChanged += handler,
                handler => PositionChanged -= handler
            );

            IObservable<Size> sizeChanged = this.GetObservable(ClientSizeProperty);

            positionChanged.Select(_ => Unit.Default).Merge(sizeChanged.Select(_ => Unit.Default)
                )
                .Throttle(TimeSpan.FromMilliseconds(2000))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Where(_ => WindowState == WindowState.Normal)
                .Subscribe(async void (_) =>
                {
                    try
                    {
                        if (ViewModel != null && WindowState == WindowState.Normal)
                        {
                            await ViewModel.SavePlacementAsync(
                                WindowState,
                                Position,
                                ClientSize);
                            Log.Debug("[MAIN-WINDOW] Rx-based dynamic save complete.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[MAIN-WINDOW] Error in Rx-based dynamic save.");
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private async Task LoadWindowPlacementAsync(MainWindowViewModel viewModel)
    {
        WindowPlacement? placement = await viewModel.LoadInitialPlacementAsync();
        if (placement == null)
        {
            return;
        }
        Log.Information("[MAIN-WINDOW] Window placement loaded: {Placement}", placement);

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
