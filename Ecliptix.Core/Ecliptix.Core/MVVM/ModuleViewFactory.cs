using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Modularity;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.MVVM;

public class ModuleViewFactory : IModuleViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();
    private readonly Dictionary<ModuleIdentifier, Type> _moduleViewModels = new();
    private readonly IModuleManager _moduleManager;
    private readonly IServiceProvider _serviceProvider;

    public ModuleViewFactory(IModuleManager moduleManager, IServiceProvider serviceProvider)
    {
        _moduleManager = moduleManager;
        _serviceProvider = serviceProvider;
    }

    public void RegisterView<TViewModel, TView>()
        where TViewModel : class
        where TView : Control, new()
    {
        Type vmType = typeof(TViewModel);
        _factories[vmType] = () => new TView();
    }

    public void RegisterModuleViewModel(ModuleIdentifier moduleId, Type viewModelType)
    {
        _moduleViewModels[moduleId] = viewModelType;
    }

    public Option<Control> CreateView(Type viewModelType)
    {
        return CreateView(viewModelType, _serviceProvider, bindViewModel: false);
    }

    private Option<Control> CreateView(Type viewModelType, IServiceProvider serviceProvider, bool bindViewModel)
    {
        if (!_factories.TryGetValue(viewModelType, out Func<Control>? factory))
        {
            return Option<Control>.None;
        }

        Control view = factory();

        if (bindViewModel)
        {
            object? viewModel = serviceProvider.GetService(viewModelType);
            if (viewModel != null)
            {
                view.DataContext = viewModel;
                Log.Debug("Created view for {ViewModelType} and bound ViewModel", viewModelType.Name);
            }
            else
            {
                Log.Warning("Could not resolve ViewModel of type {ViewModelType} from DI container", viewModelType.Name);
            }
        }

        return Option<Control>.Some(view);
    }

    public async Task<Option<UserControl>> CreateViewForModuleAsync(ModuleIdentifier moduleId)
    {
        string moduleName = moduleId.ToName();

        Option<IModule> moduleOption = await _moduleManager.LoadModuleAsync(moduleName);

        if (!moduleOption.IsSome)
        {
            Log.Error("Failed to load module: {ModuleName}", moduleName);
            return Option<UserControl>.None;
        }

        Type? viewModelType = GetViewModelTypeForModule(moduleId);
        if (viewModelType == null)
        {
            Log.Error("No ViewModel type found for module: {ModuleName}", moduleName);
            return Option<UserControl>.None;
        }

        IModule module = moduleOption.Value!;
        IServiceProvider provider = module.ServiceScope?.ServiceProvider ?? _serviceProvider;
        Option<Control> viewOption = CreateView(viewModelType, provider, bindViewModel: true);

        if (!viewOption.IsSome)
        {
            Log.Warning("No view registered for module: {ModuleName}", moduleName);
            return Option<UserControl>.None;
        }

        return Option<UserControl>.Some((UserControl)viewOption.Value!);
    }

    private Type? GetViewModelTypeForModule(ModuleIdentifier moduleId)
    {
        return _moduleViewModels.TryGetValue(moduleId, out Type? viewModelType) ? viewModelType : null;
    }
}
