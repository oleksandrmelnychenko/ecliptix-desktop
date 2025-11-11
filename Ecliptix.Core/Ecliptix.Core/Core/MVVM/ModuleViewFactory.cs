using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Chats.ViewModels;
using Ecliptix.Core.Features.Feed.ViewModels;
using Ecliptix.Core.Features.Main;
using Ecliptix.Core.Features.Settings.ViewModels;
using Ecliptix.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ModuleViewFactory : IModuleViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();
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

    public Option<Control> CreateView(Type viewModelType)
    {
        if (!_factories.TryGetValue(viewModelType, out Func<Control>? factory))
        {
            return Option<Control>.None;
        }

        Control view = factory();

        object? viewModel = _serviceProvider.GetService(viewModelType);
        if (viewModel != null)
        {
            view.DataContext = viewModel;
            Log.Debug("Created view for {ViewModelType} and bound ViewModel", viewModelType.Name);
        }
        else
        {
            Log.Warning("Could not resolve ViewModel of type {ViewModelType} from DI container", viewModelType.Name);
        }

        return Option<Control>.Some(view);
    }

    public async Task<Option<UserControl>> CreateViewForModuleAsync(ModuleIdentifier moduleId)
    {
        if (!MainModule.CanLoadContentModule(moduleId))
        {
            Log.Warning("Main module does not allow loading content module: {ModuleName}", moduleId.ToName());
            return Option<UserControl>.None;
        }

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

        Option<Control> viewOption = CreateView(viewModelType);

        if (!viewOption.IsSome)
        {
            Log.Warning("No view registered for module: {ModuleName}", moduleName);
            return Option<UserControl>.None;
        }

        return Option<UserControl>.Some((UserControl)viewOption.Value!);
    }

    private static Type? GetViewModelTypeForModule(ModuleIdentifier moduleId) => moduleId switch
    {
        ModuleIdentifier.FEED => typeof(FeedViewModel),
        ModuleIdentifier.CHATS => typeof(ChatsViewModel),
        ModuleIdentifier.SETTINGS => typeof(SettingsViewModel),
        _ => null
    };
}
