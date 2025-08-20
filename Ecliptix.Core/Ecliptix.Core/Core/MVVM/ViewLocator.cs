using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Ecliptix.Core.Core.Abstractions;
using ReactiveUI;
using Serilog;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;

namespace Ecliptix.Core.Core.MVVM;

public class ViewLocator : IViewLocator
{
    private readonly ConcurrentDictionary<Type, Type> _registrations = new();

    public void Register<TViewModel, TView>() 
        where TViewModel : class, IRoutableViewModel
        where TView : class
    {
        Register(typeof(TViewModel), typeof(TView));
    }

    public void Register(Type viewModelType, Type viewType)
    {
        if (!typeof(IRoutableViewModel).IsAssignableFrom(viewModelType))
            throw new ArgumentException($"ViewModel type {viewModelType.Name} must implement IRoutableViewModel");

        _registrations[viewModelType] = viewType;
        Log.Debug("Registered view mapping: {ViewModel} -> {View}", viewModelType.Name, viewType.Name);
    }

    public object? ResolveView<TViewModel>(TViewModel? viewModel = null) where TViewModel : class, IRoutableViewModel
    {
        return ResolveView((object?)viewModel);
    }

    public object? ResolveView(object? viewModel)
    {
        if (viewModel == null)
            return null;

        Type viewModelType = viewModel.GetType();

        if (_registrations.TryGetValue(viewModelType, out Type? viewType))
        {
            try
            {
                object? view = Activator.CreateInstance(viewType);
                Log.Debug("Resolved view {ViewType} for ViewModel {ViewModelType}", viewType.Name, viewModelType.Name);
                return view;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create view instance for {ViewType}", viewType.Name);
                return null;
            }
        }

        
        return TryConventionBasedResolution(viewModelType);
    }


    public bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel
    {
        return IsRegistered(typeof(TViewModel));
    }

    public bool IsRegistered(Type viewModelType)
    {
        return _registrations.ContainsKey(viewModelType);
    }

    public IReadOnlyDictionary<Type, Type> GetRegistrations()
    {
        return new Dictionary<Type, Type>(_registrations);
    }

    private object? TryConventionBasedResolution(Type viewModelType)
    {
        
        string viewModelName = viewModelType.Name;
        if (viewModelName.EndsWith("ViewModel"))
        {
            string viewName = viewModelName.Substring(0, viewModelName.Length - "ViewModel".Length) + "View";
            string? viewTypeNamespace = viewModelType.Namespace?.Replace(".ViewModels", ".Views");
            string viewTypeName = $"{viewTypeNamespace}.{viewName}";

            Type? viewType = Type.GetType(viewTypeName, false);
            if (viewType != null)
            {
                try
                {
                    object? view = Activator.CreateInstance(viewType);
                    Log.Debug("Convention-based view resolution: {ViewModel} -> {View}", viewModelType.Name, viewType.Name);
                    return view;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to create view instance via convention for {ViewType}", viewType.Name);
                }
            }
        }

        Log.Warning("No view found for ViewModel {ViewModelType}", viewModelType.Name);
        return null;
    }
}


