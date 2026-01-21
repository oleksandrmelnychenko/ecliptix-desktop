using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Feature.Main.ViewModels;
using Serilog;

namespace Ecliptix.Feature.Main.Views;

public partial class MasterView : UserControl
{
    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        Log.Information("[MASTER-VIEW] DataContext changed to: {Type}", DataContext?.GetType().Name ?? "null");

        if (DataContext is INotifyPropertyChanged oldNpc)
        {
            oldNpc.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (DataContext is MasterViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            Log.Information("[MASTER-VIEW] Subscribed to PropertyChanged, CurrentView={CurrentView}",
                vm.CurrentView?.GetType().Name ?? "null");
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MasterViewModel.CurrentView))
        {
            MasterViewModel? vm = sender as MasterViewModel;
            Log.Information("[MASTER-VIEW] PropertyChanged raised for CurrentView, new value: {Type}",
                vm?.CurrentView?.GetType().Name ?? "null");
        }
    }
}
