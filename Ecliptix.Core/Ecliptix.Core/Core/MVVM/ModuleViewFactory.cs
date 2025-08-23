using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ModuleViewFactory : IModuleViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();

    public int RegisteredViewCount => _factories.Count;

    public void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new()
    {
        Type vmType = typeof(TViewModel);
        Type viewType = typeof(TView);

        _factories[vmType] = () => new TView();
        Log.Debug("Registered view factory {ViewType} for ViewModel {ViewModelType}",
            viewType.Name, vmType.Name);
    }

    public Control? CreateView(Type viewModelType)
    {
        if (_factories.TryGetValue(viewModelType, out Func<Control>? factory))
        {
            Control view = factory();
            Log.Debug("Created view {ViewType} for ViewModel {ViewModelType}",
                view.GetType().Name, viewModelType.Name);
            return view;
        }

        Log.Warning("No view factory registered for ViewModel type: {ViewModelType}", viewModelType.Name);
        return null;
    }

    public bool HasView(Type viewModelType)
    {
        return _factories.ContainsKey(viewModelType);
    }
}