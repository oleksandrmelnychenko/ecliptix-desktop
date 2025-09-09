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

        Log.Warning("ViewLocator.Register is deprecated for AOT compatibility. Use StaticViewMapper instead. ViewModel: {ViewModel} -> View: {View}", viewModelType.Name, viewType.Name);
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
                Log.Information("✅ ViewLocator: Successfully created view {ViewType} for ViewModel {ViewModelType} using registered factory",
                    view.GetType().Name, viewModelType.Name);
                return view;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ ViewLocator: Failed to create view instance using factory for ViewModel {ViewModelType}", viewModelType.Name);
                return null;
            }
        }

        Log.Information("🔄 ViewLocator: No factory registered, trying StaticViewMapper for {ViewModelType}", viewModelType.Name);
        object? staticView = StaticViewMapper.CreateView(viewModelType);
        if (staticView != null)
        {
            Log.Information("✅ ViewLocator: Successfully created view {ViewType} for ViewModel {ViewModelType} using StaticViewMapper",
                staticView.GetType().Name, viewModelType.Name);
            return staticView;
        }

        Log.Warning("⚠️ ViewLocator: No view factory registered and StaticViewMapper returned null for ViewModel {ViewModelType}", viewModelType.Name);
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


