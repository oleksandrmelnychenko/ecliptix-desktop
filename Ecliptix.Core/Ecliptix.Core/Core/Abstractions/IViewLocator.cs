using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ReactiveUI;

namespace Ecliptix.Core.Core.Abstractions;

public interface IViewLocator
{
    void Register<TViewModel, TView>()
        where TViewModel : class, IRoutableViewModel
        where TView : class, new();

    [RequiresUnreferencedCode("ViewLocator uses reflection to validate ViewModel types")]
    [RequiresDynamicCode("ViewLocator may create types dynamically")]
    void Register([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type viewModelType,
                  [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type viewType);

    void RegisterFactory<TViewModel>(Func<object> factory)
        where TViewModel : class, IRoutableViewModel;

    object? ResolveView<TViewModel>(TViewModel? viewModel = null) where TViewModel : class, IRoutableViewModel;

    [RequiresUnreferencedCode("ViewLocator uses reflection to determine view model types")]
    object? ResolveView(object? viewModel);

    bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel;

    bool IsRegistered(Type viewModelType);

    IReadOnlyDictionary<Type, Func<object>> GetRegistrations();
}