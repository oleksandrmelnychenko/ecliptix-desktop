using System;
using System.Collections.Generic;
using ReactiveUI;

namespace Ecliptix.Core.Core.Abstractions;

public interface IViewLocator
{
    void Register<TViewModel, TView>() 
        where TViewModel : class, IRoutableViewModel
        where TView : class;
    
    void Register(Type viewModelType, Type viewType);
    
    object? ResolveView<TViewModel>(TViewModel? viewModel = null) where TViewModel : class, IRoutableViewModel;
    
    object? ResolveView(object? viewModel);
    
    bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel;
    
    bool IsRegistered(Type viewModelType);
    
    IReadOnlyDictionary<Type, Type> GetRegistrations();
}