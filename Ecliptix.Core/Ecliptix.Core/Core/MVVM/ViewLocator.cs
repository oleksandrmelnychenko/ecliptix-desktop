using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Ecliptix.Core.Core.Abstractions;
using ReactiveUI;
using Serilog;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;

namespace Ecliptix.Core.Core.MVVM;

public class ViewLocator : IViewLocator
{
    private readonly ConcurrentDictionary<Type, Func<object>> _viewFactories = new();

    public void Register<TViewModel, TView>()
        where TViewModel : class, IRoutableViewModel
        where TView : class, new()
    {
        RegisterFactory<TViewModel>(() => new TView());
    }

    [RequiresUnreferencedCode("ViewLocator uses reflection to validate ViewModel types")]
    [RequiresDynamicCode("ViewLocator may create types dynamically")]
    public void Register(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type viewModelType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type viewType)
    {
        if (!typeof(IRoutableViewModel).IsAssignableFrom(viewModelType))
            throw new ArgumentException($"ViewModel type {viewModelType.Name} must implement IRoutableViewModel");

        Func<object> factory = () => Activator.CreateInstance(viewType)
            ?? throw new InvalidOperationException($"Could not create instance of {viewType.Name}");
        _viewFactories[viewModelType] = factory;
    }

    public void RegisterFactory<TViewModel>(Func<object> factory)
        where TViewModel : class, IRoutableViewModel
    {
        _viewFactories[typeof(TViewModel)] = factory;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ResolveView delegates to the RequiresUnreferencedCode overload which handles reflection safely")]
    public object? ResolveView<TViewModel>(TViewModel? viewModel = null) where TViewModel : class, IRoutableViewModel
    {
        return ResolveView((object?)viewModel);
    }

    [RequiresUnreferencedCode("ViewLocator uses reflection to determine view model types")]
    public object? ResolveView(object? viewModel)
    {
        if (viewModel == null)
        {
            return null;
        }

        Type viewModelType = viewModel.GetType();

        if (_viewFactories.TryGetValue(viewModelType, out Func<object>? factory))
        {
            object view = factory();
            return view;
        }

        object? staticView = StaticViewMapper.CreateView(viewModelType);
        return staticView;
    }

    public bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel
    {
        return IsRegistered(typeof(TViewModel));
    }

    public bool IsRegistered(Type viewModelType)
    {
        return _viewFactories.ContainsKey(viewModelType);
    }

    public IReadOnlyDictionary<Type, Func<object>> GetRegistrations()
    {
        return new Dictionary<Type, Func<object>>(_viewFactories);
    }
}