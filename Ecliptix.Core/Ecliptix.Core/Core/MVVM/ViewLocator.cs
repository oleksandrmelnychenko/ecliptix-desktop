using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Ecliptix.Utilities;
using ReactiveUI;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;

namespace Ecliptix.Core.Core.MVVM;

public sealed class ViewLocator : IViewLocator
{
    private readonly ConcurrentDictionary<Type, Func<object>> _viewFactories = new();

    public void Register<TViewModel, TView>()
        where TViewModel : class, IRoutableViewModel
        where TView : class, new() =>
        RegisterFactory<TViewModel>(() => new TView());

    public void RegisterFactory<TViewModel>(Func<object> factory)
        where TViewModel : class, IRoutableViewModel =>
        _viewFactories[typeof(TViewModel)] = factory;

    public Option<object> ResolveView<TViewModel>(TViewModel? viewModel = null)
        where TViewModel : class, IRoutableViewModel
    {
        if (viewModel == null)
        {
            return Option<object>.None;
        }

        Type viewModelType = typeof(TViewModel);

        if (_viewFactories.TryGetValue(viewModelType, out Func<object>? factory))
        {
            object view = factory();
            return Option<object>.Some(view);
        }

        Type actualType = viewModel.GetType();
        if (actualType != viewModelType && _viewFactories.TryGetValue(actualType, out factory))
        {
            object view = factory();
            return Option<object>.Some(view);
        }

        object? staticView = StaticViewMapper.CreateView(actualType);
        return staticView.ToOption();
    }

    public Option<object> ResolveView(object? viewModel)
    {
        if (viewModel == null)
        {
            return Option<object>.None;
        }

        Type viewModelType = viewModel.GetType();

        if (_viewFactories.TryGetValue(viewModelType, out Func<object>? factory))
        {
            object view = factory();
            return Option<object>.Some(view);
        }

        object? staticView = StaticViewMapper.CreateView(viewModelType);
        return staticView.ToOption();
    }

    public bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel => IsRegistered(typeof(TViewModel));

    public bool IsRegistered(Type viewModelType) => _viewFactories.ContainsKey(viewModelType);

    public IReadOnlyDictionary<Type, Func<object>> GetRegistrations() => new Dictionary<Type, Func<object>>(_viewFactories);
}
