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

    public void Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type viewModelType, 
                        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type viewType)
    {
        if (!typeof(IRoutableViewModel).IsAssignableFrom(viewModelType))
            throw new ArgumentException($"ViewModel type {viewModelType.Name} must implement IRoutableViewModel");

        _viewFactories[viewModelType] = () => Activator.CreateInstance(viewType) ?? throw new InvalidOperationException($"Failed to create instance of {viewType.Name}");
        Log.Debug("Registered view mapping: {ViewModel} -> {View}", viewModelType.Name, viewType.Name);
    }

    public void RegisterFactory<TViewModel>(Func<object> factory)
        where TViewModel : class, IRoutableViewModel
    {
        _viewFactories[typeof(TViewModel)] = factory;
        Log.Debug("Registered view factory for ViewModel: {ViewModel}", typeof(TViewModel).Name);
    }

    public object? ResolveView<TViewModel>(TViewModel? viewModel = null) where TViewModel : class, IRoutableViewModel
    {
        return ResolveView((object?)viewModel);
    }

    public object? ResolveView(object? viewModel)
    {
        Log.Information("🔍 ViewLocator.ResolveView called with ViewModel: {ViewModelType}",
            viewModel?.GetType().Name ?? "null");

        if (viewModel == null)
        {
            Log.Warning("❌ ViewLocator: ViewModel is null");
            return null;
        }

        Type viewModelType = viewModel.GetType();
        Log.Information("🔍 ViewLocator: Looking for factory for {ViewModelType}", viewModelType.Name);
        Log.Information("🔍 ViewLocator: Registered factories count: {Count}", _viewFactories.Count);

        foreach (Type registeredType in _viewFactories.Keys)
        {
            Log.Information("🔍 ViewLocator: Registered type: {TypeName}", registeredType.Name);
        }

        if (_viewFactories.TryGetValue(viewModelType, out Func<object>? factory))
        {
            try
            {
                object view = factory();
                Log.Information("✅ ViewLocator: Successfully created view {ViewType} for ViewModel {ViewModelType}",
                    view.GetType().Name, viewModelType.Name);
                return view;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ ViewLocator: Failed to create view instance using factory for ViewModel {ViewModelType}", viewModelType.Name);
                return null;
            }
        }

        Log.Warning("⚠️ ViewLocator: No view factory registered for ViewModel {ViewModelType}", viewModelType.Name);
        return null;
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


