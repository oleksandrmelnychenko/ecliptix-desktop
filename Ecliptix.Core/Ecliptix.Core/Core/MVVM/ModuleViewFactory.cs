using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Feed.Views;
using Ecliptix.Core.Features.Chats.Views;
using Ecliptix.Core.Features.Main;
using Ecliptix.Core.Features.Settings.Views;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ModuleViewFactory : IModuleViewFactory
{
    private readonly Dictionary<Type, Func<Control>> _factories = new();
    private readonly IModuleManager _moduleManager;

    public ModuleViewFactory(IModuleManager moduleManager)
    {
        _moduleManager = moduleManager;
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
        return _factories.GetValueOrDefault(viewModelType).ToOption()
            .Select(factory => factory());
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

        UserControl? view = moduleId switch
        {
            ModuleIdentifier.FEED => new FeedView(),
            ModuleIdentifier.CHATS => new ChatsView(),
            ModuleIdentifier.SETTINGS => new SettingsView(),
            _ => null
        };

        if (view == null)
        {
            Log.Warning("No view registered for module: {ModuleName}", moduleName);
        }

        return view != null ? Option<UserControl>.Some(view) : Option<UserControl>.None;
    }
}
