using Avalonia.Controls;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Modularity;

public interface IModuleViewFactory
{
    void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new();

    void RegisterModuleViewModel(ModuleIdentifier moduleId, Type viewModelType);

    Option<Control> CreateView(Type viewModelType);

    Task<Option<UserControl>> CreateViewForModuleAsync(ModuleIdentifier moduleId);
}
