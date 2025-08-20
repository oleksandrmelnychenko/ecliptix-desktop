using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Core.Core.MVVM;

public class ModuleViewFactory : IModuleViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();
    private readonly ILogger<ModuleViewFactory> _logger;

    public ModuleViewFactory(ILogger<ModuleViewFactory> logger)
    {
        _logger = logger;
    }

    public int RegisteredViewCount => _factories.Count;

    public void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new()
    {
        Type vmType = typeof(TViewModel);
        Type viewType = typeof(TView);

        _factories[vmType] = () => new TView();
        _logger.LogDebug("Registered view factory {ViewType} for ViewModel {ViewModelType}",
            viewType.Name, vmType.Name);
    }

    public Control? CreateView(Type viewModelType)
    {
        if (_factories.TryGetValue(viewModelType, out Func<Control>? factory))
        {
            Control view = factory();
            _logger.LogDebug("Created view {ViewType} for ViewModel {ViewModelType}",
                view.GetType().Name, viewModelType.Name);
            return view;
        }

        _logger.LogWarning("No view factory registered for ViewModel type: {ViewModelType}", viewModelType.Name);
        return null;
    }

    public Control? CreateView<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        if (viewModel == null)
        {
            _logger.LogWarning("Cannot create view for null ViewModel");
            return null;
        }

        Control? view = CreateView(typeof(TViewModel));
        if (view != null)
        {
            view.DataContext = viewModel;
        }

        return view;
    }

    public bool HasView(Type viewModelType)
    {
        return _factories.ContainsKey(viewModelType);
    }
}