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

    public void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new()
    {
        Type vmType = typeof(TViewModel);
        _factories[vmType] = () => new TView();
    }

    public Control? CreateView(Type viewModelType)
    {
        if (!_factories.TryGetValue(viewModelType, out Func<Control>? factory)) return null;
        Control view = factory();
        return view;
    }
}
