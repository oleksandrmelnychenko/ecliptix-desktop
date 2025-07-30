using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Controls;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public class BottomSheetViewModel : ViewModelBase
{
    private bool _isVisible;
    private ObservableCollection<Control> _contentControls = new();

    public bool IsVisible
    {
        get => _isVisible;
        set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    public ObservableCollection<Control> ContentControls
    {
        get => _contentControls;
        set => this.RaiseAndSetIfChanged(ref _contentControls, value);
    }

    public ReactiveCommand<Unit, Unit> ShowCommand { get; }
    public ReactiveCommand<Unit, Unit> HideCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    public BottomSheetViewModel(ISystemEvents systemEvents,NetworkProvider networkProvider, ILocalizationService localizationService) 
        : base(systemEvents,networkProvider, localizationService)
    {
        _isVisible = false;
        ShowCommand = ReactiveCommand.Create(() => { IsVisible = true; return Unit.Default; });
        HideCommand = ReactiveCommand.Create(() => { IsVisible = false; return Unit.Default; });
        ToggleCommand = ReactiveCommand.Create(() => { IsVisible = !IsVisible; return Unit.Default; });
    }

    public void AddContent(Control control)
    {
        ContentControls.Add(control);
    }
    
    public void AddContent(List<Control> controls)
    {
        foreach (var control in controls)
        {
            ContentControls.Add(control);
        }
    }

    public void ClearContent()
    {
        ContentControls.Clear();
    }
}