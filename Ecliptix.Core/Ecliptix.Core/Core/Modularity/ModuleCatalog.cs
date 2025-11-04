using System;
using System.Collections.Generic;
using System.Linq;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Core.Modularity;

public interface IModuleCatalog
{
    void AddModule<TModule>() where TModule : class, IModule, new();
    void AddModule(IModule module);

    IReadOnlyList<IModule> GetModules();
    Option<IModule> GetModule(string name);
}

public class ModuleCatalog : IModuleCatalog
{
    private readonly List<IModule> _modules = [];

    public void AddModule<TModule>() where TModule : class, IModule, new()
    {
        IModule module = new TModule();
        AddModule(module);
    }

    public void AddModule(IModule module)
    {
        if (_modules.Any(m => m.Id == module.Id))
        {
            return;
        }

        _modules.Add(module);
    }

    public IReadOnlyList<IModule> GetModules() => _modules.AsReadOnly();

    public Option<IModule> GetModule(string name) =>
        _modules.FirstOrDefault(m => string.Equals(m.Id.ToName(), name, StringComparison.OrdinalIgnoreCase))
            .ToOption();
}
