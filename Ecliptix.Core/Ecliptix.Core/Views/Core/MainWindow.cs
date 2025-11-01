using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.ViewModels.Core;
using ReactiveUI;

namespace Ecliptix.Core.Views.Core;

public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
{
    private bool _languageSelectorLoaded;
    private readonly Border? _languageSelectorContainer;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);

        _languageSelectorContainer = this.FindControl<Border>("LanguageSelectorContainer");

        SetupLazyLanguageSelector();

#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void SetupLazyLanguageSelector()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .Where(dc => dc != null)
                .Select(dc => dc!)
                .OfType<MainWindowViewModel>()
                .Take(1)
                .Where(_ => !_languageSelectorLoaded)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(LoadLanguageSelector)
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
}
