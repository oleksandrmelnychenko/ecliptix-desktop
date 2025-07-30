using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Services;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public sealed class BottomSheetViewModel : ReactiveObject, IActivatableViewModel
{
    private bool _isVisible;
    private bool _isDismissableOnScrimClick;
    private UserControl? _content;

    public UserControl? Content
    {
        get => _content;
        set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public bool IsDismissableOnScrimClick
    {
        get => _isDismissableOnScrimClick;
        set => this.RaiseAndSetIfChanged(ref _isDismissableOnScrimClick, value);
    }

    public ViewModelActivator Activator { get; } = new();

    public ReactiveCommand<Unit, Unit> ShowCommand { get; }

    public ReactiveCommand<Unit, Unit> HideCommand { get; }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public BottomSheetViewModel(IBottomSheetEvents bottomSheetEvents, ILocalizationService localizationService)
    {
        ArgumentNullException.ThrowIfNull(bottomSheetEvents, nameof(bottomSheetEvents));
        ArgumentNullException.ThrowIfNull(localizationService, nameof(localizationService));

        _content = null;
        _isVisible = false;
        _isDismissableOnScrimClick = true;

        ShowCommand = ReactiveCommand.Create(() =>
        {
            Log.Information("Executing ShowCommand: Setting IsVisible=true");
            IsVisible = true;
            return Unit.Default;
        });

        HideCommand = ReactiveCommand.Create(() =>
        {
            Log.Information("Executing HideCommand: Setting IsVisible=false");
            IsVisible = false;
            return Unit.Default;
        });

        ToggleCommand = ReactiveCommand.Create(() =>
        {
            bool newVisibility = !IsVisible;
            Log.Information($"Executing ToggleCommand: Setting IsVisible={newVisibility}");
            IsVisible = newVisibility;
            return Unit.Default;
        });

        this.WhenActivated(disposables =>
        {
            bottomSheetEvents.BottomSheetChanged
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(eventArgs =>
                {
                    if (_content != eventArgs.Control)
                    {
                        Content = eventArgs.Control;
                    }

                    bool shouldBeVisible = eventArgs.Control != null;
                    if (_isVisible != shouldBeVisible)
                    {
                        IsVisible = shouldBeVisible;
                    }

                    bool isDismissable = eventArgs.ComponentType != BottomSheetComponentType.DetectedLocalization;
                    if (_isDismissableOnScrimClick != isDismissable)
                    {
                        IsDismissableOnScrimClick = isDismissable;
                    }
                })
                .DisposeWith(disposables);
        });
    }
}