using Ecliptix.Utilities;
using ReactiveUI;

namespace Ecliptix.Core.Modularity;

public interface IViewLocator
{
    void Register<TViewModel, TView>()
        where TViewModel : class, IRoutableViewModel
        where TView : class, new();

    void RegisterFactory<TViewModel>(Func<object> factory)
        where TViewModel : class, IRoutableViewModel;

    Option<object> ResolveView<TViewModel>(TViewModel? viewModel = null)
        where TViewModel : class, IRoutableViewModel;

    Option<object> ResolveView(object? viewModel);

    bool IsRegistered<TViewModel>() where TViewModel : class, IRoutableViewModel;

    bool IsRegistered(Type viewModelType);

    IReadOnlyDictionary<Type, Func<object>> GetRegistrations();
}
