using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Feed.Views;
using Ecliptix.Core.Features.Chats.Views;
using Ecliptix.Core.Features.Settings.Views;
using Ecliptix.Utilities;

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
        string moduleName = moduleId.ToName();

        Option<IModule> moduleOption = await _moduleManager.LoadModuleAsync(moduleName);

        if (!moduleOption.IsSome)
        {
            return Option<UserControl>.None;
        }

        UserControl? view = moduleId switch
        {
            ModuleIdentifier.FEED => new FeedView(),
            ModuleIdentifier.CHATS => new ChatsView(),
            ModuleIdentifier.SETTINGS => new SettingsView(),
            _ => null
        };

        return view != null ? Option<UserControl>.Some(view) : Option<UserControl>.None;
    }
}
