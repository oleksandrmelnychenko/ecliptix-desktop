using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships;

public partial class MembershipHostWindow : ReactiveWindow<MembershipHostWindowModel>
{
    private bool _languageSelectorLoaded;
    private readonly Border? _languageSelectorContainer;
    private readonly BottomSheetControl? _bottomSheetControl;
    private readonly Border? _mainBorder;
    private readonly Grid? _generalControlsGrid;

    public MembershipHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);

        _languageSelectorContainer = this.FindControl<Border>("LanguageSelectorContainer");
        _bottomSheetControl = this.FindControl<BottomSheetControl>("BottomSheetControl");
        _mainBorder = this.FindControl<Border>("MainBorder");
        _generalControlsGrid = this.FindControl<Grid>("GeneralConrolsGrid");

        SetupLazyLanguageSelector();
        SetupBottomSheetFocusManagement();
    }

    private void SetupLazyLanguageSelector()
    {
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DataContext)
                .Where(dc => dc != null)
                .Select(dc => dc!)
                .OfType<MembershipHostWindowModel>()
                .Take(1)
                .Where(_ => !_languageSelectorLoaded)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(LoadLanguageSelector)
                .DisposeWith(disposables);
        });
    }

    private void SetupBottomSheetFocusManagement()
    {
        this.WhenActivated(disposables =>
        {
            if (_bottomSheetControl?.ViewModel == null || _mainBorder == null || _generalControlsGrid == null) return;

            _bottomSheetControl.ViewModel
                .WhenAnyValue(x => x.IsVisible)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(isVisible =>
                {
                    if (isVisible)
                    {
                        if (!_bottomSheetControl.ViewModel.IsVisible) return;
                        _mainBorder.IsEnabled = false;
                        _generalControlsGrid.IsEnabled = false;
                        _bottomSheetControl.Focus(NavigationMethod.Tab);
                    }
                    else
                    {
                        _mainBorder.IsEnabled = true;
                        _generalControlsGrid.IsEnabled = true;
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private void LoadLanguageSelector(MembershipHostWindowModel viewModel)
    {
        if (_languageSelectorLoaded || _languageSelectorContainer == null) return;

        LanguageSelectorView languageSelector = new()
        {
            DataContext = viewModel.LanguageSelector
        };

        _languageSelectorContainer.Child = languageSelector;
        _languageSelectorLoaded = true;
    }
}