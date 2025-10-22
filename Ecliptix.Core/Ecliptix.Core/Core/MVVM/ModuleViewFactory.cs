using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities;
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

    public Option<Control> CreateView(Type viewModelType)
    {
        return _factories.GetValueOrDefault(viewModelType).ToOption()
            .Select(factory => factory());
    }
}
